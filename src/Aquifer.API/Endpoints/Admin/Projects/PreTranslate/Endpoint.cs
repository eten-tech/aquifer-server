using Aquifer.API.Common;
using Aquifer.API.Services;
using Aquifer.Common;
using Aquifer.Common.Messages.Models;
using Aquifer.Common.Messages.Publishers;
using Aquifer.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Admin.Projects.PreTranslate;

/// <summary>
/// Re-runs a started Project through AI pre-translation.
/// This replaces the developer-only workflow of manually replaying a message from the translate-project-resources poison queue.
/// Unlike the "start project" flow this does not create snapshots (a forced retranslation reads from the original snapshot) and does not
/// modify the Project's Started date.
/// </summary>
public class Endpoint(
    AquiferDbContext dbContext,
    IUserService userService,
    ITranslationMessagePublisher translationMessagePublisher,
    ILogger<Endpoint> logger)
    : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/admin/projects/{Id}/pre-translate");
        Permissions(PermissionName.RequeuePreTranslationProject);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var user = await userService.GetUserFromJwtAsync(ct);

        var project = await dbContext.Projects
            .SingleOrDefaultAsync(p => p.Id == request.Id, ct);

        if (project is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (project.Started is null)
        {
            AddError("Project has not been started. Use POST /projects/{id}/start instead.");
        }

        if (project.ProjectPlatformId != Constants.AquiferProjectPlatformId)
        {
            AddError("Only projects on the Aquifer platform can be pre-translated.");
        }

        var resourceContentIds = await ValidateAndDeduplicateResourceContentIdsAsync(request, ct);

        ThrowIfAnyErrors();

        logger.LogInformation(
            "Re-queuing pre-translation for Project ID {ProjectId} (requested by User ID {UserId}). ShouldForceRetranslation: {ShouldForceRetranslation}, ShouldSkipCompanyLeadAssignment: {ShouldSkipCompanyLeadAssignment}, ShouldSkipProjectStartedNotification: {ShouldSkipProjectStartedNotification}, Resource Content ID count: {ResourceContentIdCount}.",
            project.Id,
            user.Id,
            request.ShouldForceRetranslation,
            request.ShouldSkipCompanyLeadAssignment,
            request.ShouldSkipProjectStartedNotification,
            resourceContentIds?.Count ?? 0);

        await translationMessagePublisher.PublishTranslateProjectResourcesMessageAsync(
            new TranslateProjectResourcesMessage(
                project.Id,
                user.Id,
                request.ShouldForceRetranslation,
                request.ShouldSkipCompanyLeadAssignment,
                request.ShouldSkipProjectStartedNotification,
                resourceContentIds),
            ct);

        await SendAsync(null, StatusCodes.Status202Accepted, ct);
    }

    /// <summary>
    /// Returns the deduplicated Resource Content IDs to pre-translate, or null to pre-translate the whole Project.
    /// Adds an error for any requested ID that isn't in the Project.
    /// </summary>
    private async Task<IReadOnlyList<int>?> ValidateAndDeduplicateResourceContentIdsAsync(Request request, CancellationToken ct)
    {
        if (request.ResourceContentIds is not { Count: > 0 })
        {
            return null;
        }

        var resourceContentIds = request.ResourceContentIds.Distinct().ToList();

        var projectResourceContentIds = await dbContext.ProjectResourceContents
            .Where(prc => prc.ProjectId == request.Id && resourceContentIds.Contains(prc.ResourceContentId))
            .Select(prc => prc.ResourceContentId)
            .ToListAsync(ct);

        var invalidResourceContentIds = resourceContentIds.Except(projectResourceContentIds).ToList();
        if (invalidResourceContentIds.Count > 0)
        {
            AddError(
                $"The following Resource Content IDs are not in this project: {string.Join(", ", invalidResourceContentIds)}.");
        }

        return resourceContentIds;
    }
}
