using System.Text;
using Aquifer.AI;
using Aquifer.Common;
using Aquifer.Common.Messages;
using Aquifer.Common.Messages.Models;
using Aquifer.Common.Messages.Publishers;
using Aquifer.Common.Tiptap;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Enums;
using Aquifer.Data.Services;
using Aquifer.Jobs.Services.TranslationProcessors;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aquifer.Jobs.Subscribers;

public sealed class TranslationMessageSubscriber(
    AquiferDbContext _dbContext,
    ILogger<TranslationMessageSubscriber> _logger,
    ITranslationService _translationService,
    ITranslationProcessingService _translationPostProcessingService,
    IResourceHistoryService _resourceHistoryService,
    INotificationMessagePublisher _notificationMessagePublisher,
    IQueueClientFactory _queueClientFactory)
{
    private const string TranslateResourceFunctionName = "TranslateResourceMessageSubscriber";

    private const string TranslateProjectResourcesFunctionName = "TranslateProjectResourcesMessageSubscriber";

    private const string OrchestrateProjectResourcesTranslationFunctionName = "OrchestrateProjectResourcesTranslation";

    private const string TranslateResourceActivityFunctionName = "TranslateResourceActivity";

    private const string UpdateProjectPostTranslationActivityFunctionName = "UpdateProjectPostTranslationActivity";

    private const string TranslateLanguageResourcesMessageSubscriberFunctionName = "TranslateLanguageResourcesMessageSubscriber";

    private const string OrchestrateLanguageResourcesTranslationFunctionName = "OrchestrateLanguageResourcesTranslation";

    private const string CreateLanguageResourceContentActivityFunctionName = "CreateLanguageResourceContentActivity";

    private static readonly IReadOnlySet<TranslationOrigin> s_allowedTranslationOriginsForIndividualResourceContentTranslation =
        new HashSet<TranslationOrigin>
        {
            TranslationOrigin.CreateTranslation,
            TranslationOrigin.CommunityReviewer,
            TranslationOrigin.BasicTranslationOnly,
            TranslationOrigin.CreateAquiferization,
        };

    private static readonly TaskOptions s_durableFunctionTaskOptions =
        TaskOptions.FromRetryPolicy(new RetryPolicy(5, TimeSpan.FromSeconds(1)));

    /// <summary>
    /// Translates a single resource content item.
    /// Only used by individual resource translation flows. Not used by project translation.
    /// All resource content items to translate must have a draft resource content version with source language content
    /// which will be overwritten by the translated content.
    /// </summary>
    [Function(TranslateResourceFunctionName)]
    public async Task TranslateResourceViaMessageSubscriberAsync(
        [QueueTrigger(Queues.TranslateResource)]
        QueueMessage queueMessage,
        CancellationToken ct)
    {
        await queueMessage.ProcessAsync<TranslateResourceMessage, TranslationMessageSubscriber>(
            _logger,
            TranslateResourceFunctionName,
            ProcessAsync,
            ct);
    }

    private async Task ProcessAsync(QueueMessage queueMessage, TranslateResourceMessage message, CancellationToken ct)
    {
        if (!s_allowedTranslationOriginsForIndividualResourceContentTranslation.Contains(message.TranslationOrigin))
        {
            throw new InvalidOperationException(
                $"Invalid {nameof(TranslationOrigin)} for the {TranslateResourceFunctionName} flow: \"{message.TranslationOrigin}\".");
        }

        await TranslateResourceCoreAsync(
            message.ResourceContentId,
            message.StartedByUserId,
            message.ShouldForceRetranslation,
            message.TranslationOrigin,
            ct);
    }

    /// <summary>
    /// Translates a project's resource content items and then takes a final action on the project itself.
    /// All resource content items in the project to translate must have a draft resource content version with source language content
    /// which will be overwritten by the translated content.
    /// 
    /// This is done using the fan out/fan in pattern
    /// (see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview#fan-in-out)
    /// which requires using durable functions to maintain state.
    /// (see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-bindings#durableTaskClient-usage).
    /// </summary>
    [Function(TranslateProjectResourcesFunctionName)]
    public async Task TranslateProjectResourcesAsync(
        [QueueTrigger(Queues.TranslateProjectResources)]
        QueueMessage queueMessage,
        [DurableClient] DurableTaskClient durableTaskClient,
        CancellationToken cancellationToken)
    {
        await queueMessage.ProcessAsync<TranslateProjectResourcesMessage, TranslationMessageSubscriber>(
            _logger,
            TranslateProjectResourcesFunctionName,
            (qm, m, ct) => ProcessAsync(qm, m, durableTaskClient, ct),
            cancellationToken);
    }

    private async Task ProcessAsync(
        QueueMessage queueMessage,
        TranslateProjectResourcesMessage message,
        DurableTaskClient durableTaskClient,
        CancellationToken ct)
    {
        var project = await _dbContext.Projects
                .Where(p => p.Id == message.ProjectId)
                .FirstOrDefaultAsync(ct) ??
            throw new InvalidOperationException($"Project with ID {message.ProjectId} does not exist.");

        if (project.ProjectPlatformId != Constants.AquiferProjectPlatformId)
        {
            _logger.LogInformation(
                "Gracefully skipping translations for Project ID {ProjectId} because the project platform is not Aquifer.",
                message.ProjectId);

            return;
        }

        var projectResourceContentIds = await _dbContext.ProjectResourceContents
            .Where(prc => prc.ProjectId == message.ProjectId)
            .Select(prc => prc.ResourceContentId)
            .ToListAsync(ct);

        if (projectResourceContentIds.Count == 0)
        {
            throw new InvalidOperationException($"Project with ID {message.ProjectId} does not have any resource contents.");
        }

        // An admin re-run may restrict the run to specific resource contents.
        var resourceContentIdsToTranslate =
            FilterRequestedResourceContentIds(projectResourceContentIds, message.ResourceContentIds);

        if (resourceContentIdsToTranslate.Count == 0)
        {
            throw new InvalidOperationException(
                $"None of the requested Resource Content IDs belong to Project ID {message.ProjectId}.");
        }

        // Kick off the durable function orchestration.
        // If it fails it will put a message on the poison queue.
        var orchestrationInstanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
            OrchestrateProjectResourcesTranslationFunctionName,
            new OrchestrateProjectResourcesTranslationDto(
                message.ProjectId,
                message.StartedByUserId,
                resourceContentIdsToTranslate,
                Queues.GetPoisonQueueName(Queues.TranslateProjectResources),
                queueMessage.MessageText,
                message.ShouldForceRetranslation,
                message.ShouldSkipCompanyLeadAssignment,
                message.ShouldSkipProjectStartedNotification),
            ct);

        _logger.LogInformation(
            "Kicked off {FunctionName} (Orchestration Instance ID: {OrchestrationInstanceId}) for Project ID {ProjectId}.",
            OrchestrateProjectResourcesTranslationFunctionName,
            orchestrationInstanceId,
            message.ProjectId);
    }

    /// <summary>
    /// Durable orchestration function.
    /// This function is single-threaded and thus should not do any DB operations or any substantial processing.
    /// It should only orchestrate activities.
    /// </summary>
    [Function(OrchestrateProjectResourcesTranslationFunctionName)]
    public async Task OrchestrateProjectResourcesTranslationAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        OrchestrateProjectResourcesTranslationDto dto)
    {
        try
        {
            var translationOrigin = GetProjectTranslationOrigin(dto.ShouldForceRetranslation);

            var translateResourceTasks = dto.ProjectResourceContentIds
                .Select(resourceContentId =>
                    context.CallActivityAsync<int>(
                        TranslateResourceActivityFunctionName,
                        new TranslateResourceActivityDto(
                            resourceContentId,
                            dto.StartedByUserId,
                            translationOrigin,
                            dto.ShouldForceRetranslation),
                        s_durableFunctionTaskOptions))
                .ToList();

            await Task.WhenAll(translateResourceTasks);

            var translatedResourceContentIds = translateResourceTasks.Select(trt => trt.Result).ToList();

            await context.CallActivityAsync(
                UpdateProjectPostTranslationActivityFunctionName,
                new UpdateProjectPostTranslationActivityDto(
                    dto.ProjectId,
                    dto.StartedByUserId,
                    translatedResourceContentIds,
                    dto.ShouldSkipCompanyLeadAssignment,
                    dto.ShouldSkipProjectStartedNotification),
                s_durableFunctionTaskOptions);
        }
        catch (Exception orchestrationException)
        {
            // All the Activities orchestrated here have a retry policy.  If things are still failing after multiple retries
            // then something is very wrong. If this happens then swallow errors here to allow successful orchestration function
            // completion but publish a poison queue item for manual retry later.
            var ct = CancellationToken.None;
            try
            {
                _logger.LogError(
                    orchestrationException,
                    "Error translating resource content for Project ID {ProjectId}. A poison message will be published to \"{PoisonQueueName}\" to enable manual retry. Manual dev intervention is required.",
                    dto.ProjectId,
                    dto.PoisonQueueName);

                var translateProjectResourcesPoisonQueueClient = await _queueClientFactory.GetQueueClientAsync(dto.PoisonQueueName, ct);
                await translateProjectResourcesPoisonQueueClient.SendMessageAsync(dto.OriginalQueueMessageText, ct);
            }
            catch (Exception queuePublishingException)
            {
                // don't allow errors during poison queue publishing to fail the durable function
                _logger.LogError(
                    queuePublishingException,
                    "After an error translating resource content for Project ID {ProjectId} another error occurred when attempting to publish a poison message to \"{PoisonQueueName}\". Manual dev intervention is required to replay the message. Message text: {MessageText}",
                    dto.ProjectId,
                    dto.PoisonQueueName,
                    dto.OriginalQueueMessageText);
            }
        }
    }

    /// <summary>
    /// Returns the ResourceContentId that was successfully translated (or gracefully skipped).
    /// </summary>
    [Function(TranslateResourceActivityFunctionName)]
    public async Task<int> TranslateResourceViaActivityAsync(
        [ActivityTrigger] TranslateResourceActivityDto dto,
        FunctionContext activityContext)
    {
        await TranslateResourceCoreAsync(
            dto.ResourceContentId,
            dto.StartedByUserId,
            dto.ShouldForceRetranslation,
            dto.TranslationOrigin,
            activityContext.CancellationToken);

        return dto.ResourceContentId;
    }

    [Function(UpdateProjectPostTranslationActivityFunctionName)]
    public async Task UpdateProjectPostTranslationAsync(
        [ActivityTrigger] UpdateProjectPostTranslationActivityDto dto,
        FunctionContext activityContext)
    {
        _logger.LogInformation("Beginning post-translation project updates for Project ID {ProjectId}.", dto.ProjectId);

        var project = await _dbContext.Projects.SingleAsync(p => p.Id == dto.ProjectId);

        // An admin re-run may skip assignment, in which case a missing Company Lead is not an error because it's never read.
        if (dto.ShouldSkipCompanyLeadAssignment)
        {
            _logger.LogInformation(
                "Skipping Company Lead assignment for Project ID {ProjectId} because it was skipped for this run.",
                dto.ProjectId);
        }
        else
        {
            await AssignTranslatedResourcesToCompanyLeadAsync(dto, project, activityContext.CancellationToken);
        }

        if (dto.ShouldSkipProjectStartedNotification)
        {
            _logger.LogInformation(
                "Skipping the project started notification for Project ID {ProjectId} because it was skipped for this run.",
                dto.ProjectId);

            return;
        }

        await _notificationMessagePublisher.PublishSendProjectStartedNotificationMessageAsync(
            new SendProjectStartedNotificationMessage(dto.ProjectId),
            activityContext.CancellationToken);
    }

    private async Task AssignTranslatedResourcesToCompanyLeadAsync(
        UpdateProjectPostTranslationActivityDto dto,
        ProjectEntity project,
        CancellationToken ct)
    {
        var companyLeadUserId = project.CompanyLeadUserId ??
            throw new InvalidOperationException($"Company Lead User ID is null for Project ID {project.Id}. This should never happen.");

        // Assign all Draft Resource Content Versions in the project to the Company Lead of the Project.
        //
        // Edge case handling:
        // 1) It's possible that something else acted on the resource content version status
        // and moved it out of the TranslationAiDraftComplete/AquiferizeAiDraftComplete status between the resource finishing translation
        // and this project post-processing. If so, ignore those resources when performing assignments because there might already have been
        // a user assignment.
        // 2) Skip assignment if the resource is already assigned (probably by a previous run of this function).
        var resourceContentVersionsToAssign = await _dbContext.ResourceContentVersions
            .AsTracking()
            .Include(rcv => rcv.ResourceContent)
            .Where(rcv => rcv.ResourceContent.ProjectResourceContents.Any(prc => prc.Project.Id == project.Id) &&
                rcv.IsDraft &&
                (rcv.ResourceContent.Status == ResourceContentStatus.TranslationAiDraftComplete ||
                    rcv.ResourceContent.Status == ResourceContentStatus.AquiferizeAiDraftComplete ||
                    rcv.ResourceContent.Status == ResourceContentStatus.New) &&
                rcv.AssignedUserId == null)
            .ToListAsync(ct);

        // Edge case handling: Only update resource content versions that were queued for translation (even if gracefully skipped)
        // by this fan out -> fan in orchestration process.
        // Due to an unknown amount of time passing between translation and this post-processing it's unlikely but still possible
        // that the currently list of Draft Resource Content Versions is different from the list upon which the function originally acted.
        foreach (var resourceContentVersionToAssign in resourceContentVersionsToAssign.Where(rcv =>
            dto.TranslatedProjectResourceContentIds.Contains(rcv.ResourceContentId)))
        {
            // now that ALL translations are complete for the project, assign all resources in the project to the company lead
            resourceContentVersionToAssign.AssignedUserId = companyLeadUserId;
            await _resourceHistoryService.AddAssignedUserHistoryAsync(
                resourceContentVersionToAssign,
                resourceContentVersionToAssign.AssignedUserId,
                dto.StartedByUserId,
                ct);
        }

        if (resourceContentVersionsToAssign.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Translates all resources under a parent resource that have not yet been translated to a target language and do not yet
    /// have a resource content item or resource content version for that language.
    /// Translated resource content versions will be immediately published
    /// and the created resource content item will be marked as Complete.
    /// 
    /// Translation is done using the fan out/fan in pattern
    /// (see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview#fan-in-out)
    /// which requires using durable functions to maintain state.
    /// (see https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-bindings#durableTaskClient-usage).
    /// </summary>
    [Function(TranslateLanguageResourcesMessageSubscriberFunctionName)]
    public async Task TranslateLanguageResourcesAsync(
        [QueueTrigger(Queues.TranslateLanguageResources)]
        QueueMessage queueMessage,
        [DurableClient] DurableTaskClient durableTaskClient,
        CancellationToken cancellationToken)
    {
        await queueMessage.ProcessAsync<TranslateLanguageResourcesMessage, TranslationMessageSubscriber>(
            _logger,
            TranslateLanguageResourcesMessageSubscriberFunctionName,
            (qm, m, ct) => ProcessAsync(qm, m, durableTaskClient, ct),
            cancellationToken);
    }

    private async Task ProcessAsync(
        QueueMessage queueMessage,
        TranslateLanguageResourcesMessage message,
        DurableTaskClient durableTaskClient,
        CancellationToken ct)
    {
        var parentResource = await _dbContext.ParentResources
                .Where(pr => pr.Id == message.ParentResourceId)
                .SingleOrDefaultAsync(ct) ??
            throw new InvalidOperationException($"Parent Resource ID {message.ParentResourceId} does not exist.");

        if (message.SourceLanguageId == message.TargetLanguageId)
        {
            throw new InvalidOperationException(
                $"Source Language ID {message.SourceLanguageId} cannot be the same as Target Language ID {message.TargetLanguageId}.");
        }

        var languages = await _dbContext.Languages
            .Where(l => l.Id == message.SourceLanguageId || l.Id == message.TargetLanguageId)
            .ToListAsync(ct);

        var sourceLanguage = languages.SingleOrDefault(l => l.Id == message.SourceLanguageId) ??
            throw new InvalidOperationException($"Source Language ID {message.SourceLanguageId} does not exist.");

        var targetLanguage = languages.SingleOrDefault(l => l.Id == message.TargetLanguageId) ??
            throw new InvalidOperationException($"Target Language ID {message.TargetLanguageId} does not exist.");

        var sourceResourceContentVersionsToTranslate = await _dbContext.ResourceContentVersions
            .Where(rcv =>
                rcv.IsPublished &&
                rcv.ResourceContent.LanguageId == sourceLanguage.Id &&
                rcv.ResourceContent.MediaType == ResourceContentMediaType.Text &&
                rcv.ResourceContent.Resource.ParentResourceId == parentResource.Id)
            .ToListAsync(ct);

        if (sourceResourceContentVersionsToTranslate.Count == 0)
        {
            _logger.LogInformation(
                "Gracefully skipping translations for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId} because there are no resources to translate.",
                sourceLanguage.Id,
                targetLanguage.Id,
                parentResource.Id);

            return;
        }

        // Kick off the durable function orchestration.
        // If it fails it will put a message on the poison queue.
        var orchestrationInstanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
            OrchestrateLanguageResourcesTranslationFunctionName,
            new OrchestrateLanguageResourcesTranslationDto(
                sourceLanguage.Id,
                targetLanguage.Id,
                message.ParentResourceId,
                message.StartedByUserId,
                sourceResourceContentVersionsToTranslate.Select(rcv => rcv.Id).Order().ToList(),
                Queues.GetPoisonQueueName(Queues.TranslateLanguageResources),
                queueMessage.MessageText),
            ct);

        _logger.LogInformation(
            "Kicked off {FunctionName} (Orchestration Instance ID: {OrchestrationInstanceId}) for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId}.",
            OrchestrateLanguageResourcesTranslationFunctionName,
            orchestrationInstanceId,
            sourceLanguage.Id,
            targetLanguage.Id,
            message.ParentResourceId);
    }

    /// <summary>
    /// Durable orchestration function.
    /// This function is single-threaded and thus should not do any DB operations or any substantial processing.
    /// It should only orchestrate activities.
    /// </summary>
    [Function(OrchestrateLanguageResourcesTranslationFunctionName)]
    public async Task OrchestrateLanguageResourcesTranslationAsync(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        OrchestrateLanguageResourcesTranslationDto dto)
    {
        try
        {
            var createAndTranslateResourceContentTasks = dto.SourceResourceContentVersionIds
                .Select(async sourceResourceContentVersionId =>
                {
                    var resourceContentIdToTranslate = await context.CallActivityAsync<int?>(
                        CreateLanguageResourceContentActivityFunctionName,
                        new CreateLanguageResourceContentActivityDto(
                            dto.SourceLanguageId,
                            dto.TargetLanguageId,
                            sourceResourceContentVersionId,
                            dto.StartedByUserId),
                        s_durableFunctionTaskOptions);

                    if (resourceContentIdToTranslate != null)
                    {
                        return await context.CallActivityAsync<int>(
                            TranslateResourceActivityFunctionName,
                            new TranslateResourceActivityDto(
                                resourceContentIdToTranslate.Value,
                                dto.StartedByUserId,
                                TranslationOrigin.Language),
                            s_durableFunctionTaskOptions);
                    }

                    return (int?)null;
                })
                .ToList();

            var translatedResourceContentIds = (await Task.WhenAll(createAndTranslateResourceContentTasks))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            if (translatedResourceContentIds.Count > 0)
            {
                _logger.LogInformation(
                    "Successfully translated resource contents for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId}. Translated Resource Content IDs: {TranslatedResourceContentIds}.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.ParentResourceId,
                    string.Join(", ", translatedResourceContentIds));
            }
            else
            {
                _logger.LogInformation(
                    "There were no untranslated resource contents for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId}.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.ParentResourceId);
            }
        }
        catch (Exception orchestrationException)
        {
            // All the Activities orchestrated here have a retry policy.  If things are still failing after multiple retries
            // then something is very wrong. If this happens then swallow errors here to allow successful orchestration function
            // completion but publish a poison queue item for manual retry later.
            var ct = CancellationToken.None;
            try
            {
                _logger.LogError(
                    orchestrationException,
                    "Error translating resource content for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId}. A poison message will be published to \"{PoisonQueueName}\" to enable manual retry. Manual dev intervention is required.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.ParentResourceId,
                    dto.PoisonQueueName);

                var translateLanguageResourcesPoisonQueueClient = await _queueClientFactory.GetQueueClientAsync(dto.PoisonQueueName, ct);
                await translateLanguageResourcesPoisonQueueClient.SendMessageAsync(dto.OriginalQueueMessageText, ct);
            }
            catch (Exception queuePublishingException)
            {
                // don't allow errors during poison queue publishing to fail the durable function
                _logger.LogError(
                    queuePublishingException,
                    "After an error translating resource content for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Parent Resource ID {ParentResourceId} another error occurred when attempting to publish a poison message to \"{PoisonQueueName}\". Manual dev intervention is required to replay the message. Message text: {MessageText}",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.ParentResourceId,
                    dto.PoisonQueueName,
                    dto.OriginalQueueMessageText);
            }
        }
    }

    /// <summary>
    /// Returns the ResourceContentId to translate or <c>null</c> if there is no translation needed.
    /// </summary>
    [Function(CreateLanguageResourceContentActivityFunctionName)]
    public async Task<int?> CreateLanguageResourceContentAsync(
        [ActivityTrigger] CreateLanguageResourceContentActivityDto dto,
        FunctionContext activityContext)
    {
        var ct = activityContext.CancellationToken;

        var sourceResourceContentVersionToTranslate = await _dbContext.ResourceContentVersions
                .Where(rcv =>
                    rcv.Id == dto.SourceResourceContentVersionId &&
                    rcv.IsPublished &&
                    rcv.ResourceContent.LanguageId == dto.SourceLanguageId &&
                    rcv.ResourceContent.MediaType == ResourceContentMediaType.Text)
                .Include(x => x.ResourceContent)
                .SingleOrDefaultAsync(ct) ??
            throw new InvalidOperationException(
                $"Published Source Resource Content Version not found for ID {dto.SourceResourceContentVersionId}.");

        // find resource contents to potentially translate
        var existingResourceContentVersionsForLanguage = await _dbContext.ResourceContentVersions
            .Where(rcv =>
                (rcv.IsDraft || rcv.IsPublished) &&
                rcv.ResourceContent.LanguageId == dto.TargetLanguageId &&
                rcv.ResourceContent.MediaType == ResourceContentMediaType.Text &&
                rcv.ResourceContent.Resource.ResourceContents.Any(rc => rc.Id == sourceResourceContentVersionToTranslate.ResourceContentId))
            .Include(x => x.ResourceContent)
            .ThenInclude(x => x.ProjectResourceContents)
            .ToListAsync(ct);

        ResourceContentVersionEntity resourceContentVersionToTranslate;

        // There are four scenarios to handle:
        // 1. Expected case: There is no Resource Content or Resource Content Version for the given language,
        // and we need to create them here.
        // 2. Edge case: This job failed between creating the Resource Content and Resource Content Version entities and performing
        // the translation (there are two separate DB saves). For that scenario we can skip the creation of the entities and
        // jump right to translation. Note: The Resource Content must not be in any project.
        // 3. Edge case: This job already executed successfully but somehow got run again, in which case we can gracefully
        // skip doing any work for this method.
        // 4. Expected case: A manually created translation is in progress or data is not as expected, in which case we can gracefully skip
        // doing any work for this method because we run this processing for all resource contents so it's expected that some might be
        // in a manual (human) translation flow.
        //
        // Handle existing data first:
        if (existingResourceContentVersionsForLanguage.Count > 0)
        {
            var existingDraftResourceContentVersion =
                existingResourceContentVersionsForLanguage.SingleOrDefault(rcv => rcv.IsDraft);

            var existingPublishedResourceContentVersion =
                existingResourceContentVersionsForLanguage.SingleOrDefault(rcv => rcv.IsPublished);

            // Case 2: Entities were created already but no translation yet. Skip entity creation and go straight to translation.
            if (existingDraftResourceContentVersion != null &&
                existingPublishedResourceContentVersion == null &&
                existingDraftResourceContentVersion.ResourceContent.Status == ResourceContentStatus.TranslationAwaitingAiDraft &&
                existingDraftResourceContentVersion.ResourceContent.ProjectResourceContents.Count == 0)
            {
                _logger.LogInformation(
                    "Gracefully skipping creation of Resource Content item and Resource Content Version for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Source Resource Content Version ID {SourceResourceContentVersionId} because entities already exist. Resource Content Version ID: {ResourceContentVersionId}.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.SourceResourceContentVersionId,
                    existingDraftResourceContentVersion.Id);

                resourceContentVersionToTranslate = existingDraftResourceContentVersion;
            }
            // Case 3: Translation completed already so skip everything.
            else if (existingPublishedResourceContentVersion != null &&
                existingDraftResourceContentVersion == null &&
                existingPublishedResourceContentVersion.ResourceContent.Status == ResourceContentStatus.Complete &&
                existingPublishedResourceContentVersion.ResourceContent.ProjectResourceContents.Count == 0)
            {
                _logger.LogInformation(
                    "Gracefully skipping translation for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Source Resource Content Version ID {SourceResourceContentVersionId} because a translation was already completed (probably by this job). Resource Content Version ID: {ResourceContentVersionId}.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.SourceResourceContentVersionId,
                    existingPublishedResourceContentVersion.Id);

                return null;
            }
            // Case 4: Data isn't in a state that can be handled by this job so skip everything.
            else
            {
                _logger.LogInformation(
                    "Gracefully skipping translation for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Source Resource Content Version ID {SourceResourceContentVersionId} because a Resource Content Version already exists that was likely created manually (not via this job). Resource Content Version ID: {ResourceContentVersionId}.",
                    dto.SourceLanguageId,
                    dto.TargetLanguageId,
                    dto.SourceResourceContentVersionId,
                    existingDraftResourceContentVersion?.Id ?? existingPublishedResourceContentVersion!.Id);

                return null;
            }
        }
        // Case 1: No data exists. Create entities for translation.
        else
        {
            _logger.LogInformation(
                "Creating ResourceContent item and ResourceContentVersion for Source Language ID {SourceLanguageId}, Target Language ID {TargetLanguageId}, and Source Resource Content Version ID {SourceResourceContentVersionId}.",
                dto.SourceLanguageId,
                dto.TargetLanguageId,
                dto.SourceResourceContentVersionId);

            // The ResourceContentVersion and ResourceContent item created here should closely mirror what is created by the
            // CreateTranslation endpoint, though for this flow no user will be assigned.
            var newResourceContentVersion = new ResourceContentVersionEntity
            {
                Content = sourceResourceContentVersionToTranslate.Content,
                ContentSize = sourceResourceContentVersionToTranslate.ContentSize,
                DisplayName = sourceResourceContentVersionToTranslate.DisplayName,
                IsPublished = false,
                IsDraft = true,
                ReviewLevel = ResourceContentVersionReviewLevel.Ai,
                SourceWordCount = sourceResourceContentVersionToTranslate.WordCount,
                Version = 1,
                WordCount = sourceResourceContentVersionToTranslate.WordCount,
            };

            var newResourceContent = new ResourceContentEntity
            {
                ExternalVersion = sourceResourceContentVersionToTranslate.ResourceContent.ExternalVersion,
                SourceLanguageId = dto.SourceLanguageId,
                LanguageId = dto.TargetLanguageId,
                MediaType = sourceResourceContentVersionToTranslate.ResourceContent.MediaType,
                ResourceId = sourceResourceContentVersionToTranslate.ResourceContent.ResourceId,
                Status = ResourceContentStatus.TranslationAwaitingAiDraft,
                Trusted = true,
                Versions = [newResourceContentVersion],
            };

            _dbContext.ResourceContents.Add(newResourceContent);

            await _resourceHistoryService.AddStatusHistoryAsync(
                newResourceContentVersion,
                newResourceContent.Status,
                dto.StartedByUserId,
                activityContext.CancellationToken
            );

            await _resourceHistoryService.AddSnapshotHistoryAsync(
                newResourceContentVersion,
                dto.StartedByUserId,
                ResourceContentStatus.New,
                ct);

            // save to the DB before translation so that there are realized PK IDs for the new entities
            await _dbContext.SaveChangesAsync(activityContext.CancellationToken);

            resourceContentVersionToTranslate = newResourceContentVersion;

            _logger.LogInformation(
                "Created Resource Content Version {ResourceContentVersionId} and ResourceContent {ResourceContentId}.",
                resourceContentVersionToTranslate.ResourceContentId,
                resourceContentVersionToTranslate.Id);
        }

        return resourceContentVersionToTranslate.ResourceContentId;
    }

    /// <summary>
    /// Note: Callers are responsible for performing resource assignments and creating original resource content snapshots.
    /// This method will perform the actual translation, create machine translations,
    /// update the existing draft resource version with translated content, create a new snapshot for the translation,
    /// and change the resource content status.
    /// </summary>
    public async Task TranslateResourceCoreAsync(
        int resourceContentId,
        int startedByUserId,
        bool shouldForceRetranslation,
        TranslationOrigin translationOrigin,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Beginning translation for Resource Content ID {ResourceContentId} (started by User ID {StartedByUserId} via Translation Origin: {TranslationOrigin}).",
            resourceContentId,
            startedByUserId,
            translationOrigin);

        if (translationOrigin == TranslationOrigin.None)
        {
            throw new InvalidOperationException($"Translation Origin should never be \"{translationOrigin}\".");
        }

        if (shouldForceRetranslation && translationOrigin != TranslationOrigin.BasicTranslationOnly)
        {
            throw new InvalidOperationException(
                $"Translation Origin must be {TranslationOrigin.BasicTranslationOnly} when {nameof(shouldForceRetranslation)} is true but was \"{translationOrigin}\".");
        }

        var resourceContentVersion = await _dbContext.ResourceContentVersions
                .AsTracking()
                .Include(rcv => rcv.ResourceContent)
                .Include(rcv => rcv.ResourceContent.Language)
                .Include(rcv => rcv.ResourceContentVersionSnapshots)
                .SingleOrDefaultAsync(
                    rcv =>
                        rcv.IsDraft &&
                        rcv.ResourceContent.MediaType == ResourceContentMediaType.Text &&
                        rcv.ResourceContent.Id == resourceContentId,
                    ct) ??
            throw new InvalidOperationException(
                $"Aborting translation for Resource Content ID {resourceContentId} because it has no Resource Content Version with Text Media Type in the Draft status.");

        var isAquiferization = Constants.AquiferizationStatuses.Contains(resourceContentVersion.ResourceContent.Status);
        var awaitingStatus = isAquiferization
            ? ResourceContentStatus.AquiferizeAwaitingAiDraft
            : ResourceContentStatus.TranslationAwaitingAiDraft;
        var completeStatus = isAquiferization
            ? ResourceContentStatus.AquiferizeAiDraftComplete
            : ResourceContentStatus.TranslationAiDraftComplete;
        var editorStatus = isAquiferization ? ResourceContentStatus.AquiferizeEditorReview : ResourceContentStatus.TranslationEditorReview;

        if (resourceContentVersion.ResourceContentVersionSnapshots.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aborting translation for Resource Content ID {resourceContentId} because there are no pre-existing Snapshots for Resource Content Version ID {resourceContentVersion.Id}.");
        }

        if (translationOrigin != TranslationOrigin.BasicTranslationOnly && resourceContentVersion.ResourceContent.Status != awaitingStatus)
        {
            _logger.LogInformation(
                "Gracefully skipping translation for Resource Content ID {ResourceContentId} because it is not in the {ExpectedStatus} status.",
                resourceContentId,
                awaitingStatus.ToString());

            return;
        }

        // Translation should not have existing content (if not a forced retranslation) but Aquiferization may have existing content.
        if (!shouldForceRetranslation && !isAquiferization && resourceContentVersion.ResourceContent.ContentUpdated != null)
        {
            _logger.LogInformation(
                "Gracefully skipping translation for Resource Content ID {ResourceContentId} because it already has updated content.",
                resourceContentId);

            return;
        }

        var resourceContentLanguage = resourceContentVersion.ResourceContent.Language;
        var destinationLanguage = (Iso6393Code: resourceContentLanguage.ISO6393Code, EnglishName: resourceContentLanguage.EnglishDisplay);

        var translationPairs = isAquiferization || resourceContentVersion.ResourceContent.SourceLanguageId != Constants.EnglishLanguageId
            ? []
            : await _dbContext.TranslationPairs
                .Where(x => x.LanguageId == resourceContentLanguage.Id)
                .Select(x => new
                {
                    x.Key,
                    x.Value,
                })
                .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        // if this is a forced retranslation then use the original snapshot data instead of the ResourceContentVersion's data
        var firstResourceContentVersionSnapshot = resourceContentVersion.ResourceContentVersionSnapshots
            .OrderBy(rcvs => rcvs.Created)
            .First();

        var displayNameToTranslate = !shouldForceRetranslation
            ? resourceContentVersion.DisplayName
            : firstResourceContentVersionSnapshot.DisplayName;

        var contentToTranslate = !shouldForceRetranslation ? resourceContentVersion.Content : firstResourceContentVersionSnapshot.Content;

        // translate the display name unless aquiferizing
        var translatedDisplayName = displayNameToTranslate;

        if (!isAquiferization)
        {
            try
            {
                translatedDisplayName =
                    await _translationService.TranslateTextAsync(displayNameToTranslate, destinationLanguage, translationPairs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "An error occurred during translation of the display name. Resource Content ID: {ResourceContentId}; Resource Content Version ID: {ResourceContentVersionId}; Should Force Retranslation: {ShouldForceRetranslation}; Translation Origin: {TranslationOrigin}.",
                    resourceContentId,
                    resourceContentVersion.Id,
                    shouldForceRetranslation,
                    translationOrigin);
                throw;
            }
        }

        // Translate the resource content.
        // Note: Resource content is saved as Tiptap JSON in the DB.
        // It must be converted to HTML in order to be translated and then converted back to JSON after translation.
        // Some Resource Content, such as FIA, also has multiple steps.
        string translatedContentJson;
        int wordCount;
        try
        {
            var contentHtmlItems = TiptapConverter.ConvertJsonToHtmlItems(contentToTranslate);

            // translate each Tiptap step independently
            var translatedContentHtmlItems = new List<string>();
            for (var contentIndex = 0; contentIndex < contentHtmlItems.Count; contentIndex++)
            {
                var contentHtmlItem = contentHtmlItems[contentIndex];

                // validate source HTML and log if there are any issues
                var sourceHtmlValidationErrors = HtmlUtilities.GetHtmlValidationErrors(contentHtmlItem);
                if (sourceHtmlValidationErrors.Count > 0)
                {
                    _logger.LogError(
                        "HTML validation errors found in source content for Resource Content ID {ResourceContentId} and Resource Content Version ID {ResourceContentVersionId} during translation. Source HTML Validation errors: {HtmlValidationErrors}.",
                        resourceContentId,
                        resourceContentVersion.Id,
                        sourceHtmlValidationErrors);
                }

                var translatedContentHtmlItem =
                    await _translationService.TranslateHtmlAsync(
                        contentHtmlItem,
                        destinationLanguage,
                        translationPairs,
                        isAquiferization,
                        ct);

                // validate translated HTML and log if there are any issues
                var translatedHtmlValidationErrors = HtmlUtilities.GetHtmlValidationErrors(contentHtmlItem);
                if (translatedHtmlValidationErrors.Count > 0)
                {
                    _logger.LogError(
                        "HTML validation errors found in translated content for Resource Content ID {ResourceContentId} and Resource Content Version ID {ResourceContentVersionId} during translation. Source HTML also had errors: {IsSourceInvalid}. Translated HTML validation errors: {HtmlValidationErrors}.",
                        resourceContentId,
                        resourceContentVersion.Id,
                        sourceHtmlValidationErrors.Count > 0,
                        translatedHtmlValidationErrors);
                }

                // each step gets its own machine translation record
                _dbContext.ResourceContentVersionMachineTranslations.Add(
                    new ResourceContentVersionMachineTranslationEntity
                    {
                        ResourceContentVersionId = resourceContentVersion.Id,
                        DisplayName = translatedDisplayName,
                        Content = translatedContentHtmlItem,
                        ContentIndex = contentIndex,
                        UserId = null,
                        SourceId = MachineTranslationSourceId.OpenAi,
                        RetranslationReason = null,
                    });

                // some languages require additional post-processing of content
                var postProcessedContentHtmlItem = await _translationPostProcessingService.PostProcessHtmlAsync(
                    translatedContentHtmlItem,
                    destinationLanguage.Iso6393Code,
                    ct);

                translatedContentHtmlItems.Add(postProcessedContentHtmlItem);
            }

            wordCount = translatedContentHtmlItems.Sum(HtmlUtilities.GetWordCount);
            translatedContentJson = TiptapConverter.ConvertHtmlItemsToJson(translatedContentHtmlItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred during translation of the HTML content. Resource Content ID: {ResourceContentId}; Resource Content Version ID: {ResourceContentVersionId}; Should Force Retranslation: {ShouldForceRetranslation}; Translation Origin: {TranslationOrigin}.",
                resourceContentId,
                resourceContentVersion.Id,
                shouldForceRetranslation,
                translationOrigin);
            throw;
        }

        resourceContentVersion.DisplayName = translatedDisplayName;
        resourceContentVersion.Content = translatedContentJson;
        resourceContentVersion.ContentSize = Encoding.UTF8.GetByteCount(translatedContentJson);
        resourceContentVersion.WordCount = wordCount;

        resourceContentVersion.ResourceContent.ContentUpdated = DateTime.UtcNow;

        // If not a retranslation then save a new snapshot of the translation.
        // Always save it as TranslationAwaitingAiDraft/AquiferizeAwaitingAiDraft in order to prevent a user's name from appearing with the snapshot.
        if (!shouldForceRetranslation)
        {
            await _resourceHistoryService.AddSnapshotHistoryAsync(resourceContentVersion, startedByUserId, awaitingStatus, ct);
        }
        // If a retranslation then update the existing snapshot to not create too many snapshots.
        else
        {
            var existingAiTranslationSnapshot = resourceContentVersion.ResourceContentVersionSnapshots
                .OrderBy(rcvs => rcvs.Created)
                .Last(rcvs => rcvs.Status == awaitingStatus);

            existingAiTranslationSnapshot.Content = resourceContentVersion.Content;
            existingAiTranslationSnapshot.DisplayName = resourceContentVersion.DisplayName;
            existingAiTranslationSnapshot.WordCount = resourceContentVersion.WordCount;
            existingAiTranslationSnapshot.UserId = startedByUserId;
            existingAiTranslationSnapshot.Created = DateTime.UtcNow;
        }

        // basic translations should not change the status unless transitioning from AI Draft to AI Draft Complete
        if (translationOrigin != TranslationOrigin.BasicTranslationOnly || resourceContentVersion.ResourceContent.Status == awaitingStatus)
        {
            resourceContentVersion.ResourceContent.Status = completeStatus;
            await _resourceHistoryService.AddStatusHistoryAsync(
                resourceContentVersion,
                resourceContentVersion.ResourceContent.Status,
                startedByUserId,
                ct);
        }

        // if an individual translation was requested then the status needs to be updated (again) now that translation has completed
        if (translationOrigin is TranslationOrigin.CreateTranslation or TranslationOrigin.CommunityReviewer
            or TranslationOrigin.CreateAquiferization)
        {
            resourceContentVersion.ResourceContent.Status = editorStatus;
            await _resourceHistoryService.AddStatusHistoryAsync(
                resourceContentVersion,
                resourceContentVersion.ResourceContent.Status,
                startedByUserId,
                ct);

            // The resource content version should already be assigned but if it isn't then assign it to
            // the user who requested the translation.
            if (resourceContentVersion.AssignedUserId == null)
            {
                resourceContentVersion.AssignedUserId = startedByUserId;

                await _resourceHistoryService.AddAssignedUserHistoryAsync(
                    resourceContentVersion,
                    resourceContentVersion.AssignedUserId,
                    startedByUserId,
                    ct);
            }
        }

        // Language translations are fully automated. Thus, if a language translation was requested then the status needs to be updated
        // (again) to Complete now that translation has completed and the ResourceContentVersion needs to be published.
        if (translationOrigin == TranslationOrigin.Language)
        {
            // Publish the ResourceContentVersion. This code should closely mirror the Publish endpoint.
            resourceContentVersion.IsDraft = false;
            resourceContentVersion.IsPublished = true;
            resourceContentVersion.Content = SanitizeTiptapContent(resourceContentVersion.Content);

            // mark the ResourceContent item as Complete
            resourceContentVersion.ResourceContent.Status = ResourceContentStatus.Complete;
            await _resourceHistoryService.AddStatusHistoryAsync(
                resourceContentVersion,
                resourceContentVersion.ResourceContent.Status,
                startedByUserId,
                ct);
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Successfully finished translation. Resource Content ID: {ResourceContentId}; Resource Content Version ID: {ResourceContentVersionId}; Language Code: {LanguageCode}.",
            resourceContentId,
            resourceContentVersion.Id,
            resourceContentLanguage.ISO6393Code);
    }

    /// <summary>
    /// Selects the <see cref="TranslationOrigin" /> used by the project pre-translation fan out.
    /// Forced retranslation must use <see cref="TranslationOrigin.BasicTranslationOnly" />; see the guard in
    /// <see cref="TranslateResourceCoreAsync" />.
    /// </summary>
    internal static TranslationOrigin GetProjectTranslationOrigin(bool shouldForceRetranslation)
    {
        return shouldForceRetranslation ? TranslationOrigin.BasicTranslationOnly : TranslationOrigin.Project;
    }

    /// <summary>
    /// Restricts a project's Resource Content IDs to those requested by an admin re-run, ignoring any requested IDs that don't belong to
    /// the project. A null or empty request means the whole project.
    /// </summary>
    internal static IReadOnlyList<int> FilterRequestedResourceContentIds(
        IReadOnlyList<int> projectResourceContentIds,
        IReadOnlyList<int>? requestedResourceContentIds)
    {
        if (requestedResourceContentIds is not { Count: > 0 })
        {
            return projectResourceContentIds;
        }

        return projectResourceContentIds.Intersect(requestedResourceContentIds).ToList();
    }

    private static string SanitizeTiptapContent(string content)
    {
        // Remove inline comments or anything else that needs to be sanitized.
        var deserializedContent = TiptapConverter.DeserializeForPublish(content);
        return JsonUtilities.DefaultSerialize(deserializedContent);
    }

    public sealed record OrchestrateProjectResourcesTranslationDto(
        int ProjectId,
        int StartedByUserId,
        IReadOnlyList<int> ProjectResourceContentIds,
        string PoisonQueueName,
        string OriginalQueueMessageText,
        bool ShouldForceRetranslation = false,
        bool ShouldSkipCompanyLeadAssignment = false,
        bool ShouldSkipProjectStartedNotification = false);

    public sealed record TranslateResourceActivityDto(
        int ResourceContentId,
        int StartedByUserId,
        TranslationOrigin TranslationOrigin,
        bool ShouldForceRetranslation = false);

    public sealed record UpdateProjectPostTranslationActivityDto(
        int ProjectId,
        int StartedByUserId,
        IReadOnlyList<int> TranslatedProjectResourceContentIds,
        bool ShouldSkipCompanyLeadAssignment = false,
        bool ShouldSkipProjectStartedNotification = false);

    public sealed record OrchestrateLanguageResourcesTranslationDto(
        int SourceLanguageId,
        int TargetLanguageId,
        int ParentResourceId,
        int StartedByUserId,
        IReadOnlyList<int> SourceResourceContentVersionIds,
        string PoisonQueueName,
        string OriginalQueueMessageText);

    public sealed record CreateLanguageResourceContentActivityDto(
        int SourceLanguageId,
        int TargetLanguageId,
        int SourceResourceContentVersionId,
        int StartedByUserId);
}