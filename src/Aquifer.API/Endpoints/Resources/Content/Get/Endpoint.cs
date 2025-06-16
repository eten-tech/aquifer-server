using Aquifer.API.Common;
using Aquifer.API.Common.Dtos;
using Aquifer.API.Services;
using Aquifer.Common;
using Aquifer.Common.Extensions;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Resources.Content.Get;

public class Endpoint(AquiferDbContext dbContext, IUserService userService) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/resources/content/{Id}");
        Permissions(PermissionName.ReadResources);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var currentUserId = (await userService.GetUserFromJwtAsync(ct)).Id;
        var resourceContent = await dbContext.ResourceContents
            .Where(x => x.Id == request.Id)
            .Select(rc => new Response
            {
                EnglishLabel = rc.Resource.EnglishLabel,
                ParentResourceName = rc.Resource.ParentResource.DisplayName,
                ParentResourceLicenseInfo =
                    JsonUtilities.DefaultDeserialize<ParentResourceLicenseInfoSchema>(rc.Resource.ParentResource.LicenseInfo),
                ResourceContentId = rc.Id,
                ResourceId = rc.ResourceId,
                Language = new LanguageResponse
                {
                    Id = rc.LanguageId,
                    EnglishDisplay = rc.Language.EnglishDisplay,
                    ISO6393Code = rc.Language.ISO6393Code,
                    ScriptDirection = rc.Language.ScriptDirection,
                },
                Status = rc.Status,
                MediaType = rc.MediaType,
                ContentTranslations =
                    rc.Resource
                        .ResourceContents
                        .Where(orc => orc.MediaType == rc.MediaType &&
                            orc.Status != ResourceContentStatus.TranslationNotApplicable &&
                            orc.Status != ResourceContentStatus.CompleteNotApplicable)
                        .Select(orc => new TranslationResponse
                        {
                            ContentId = orc.Id,
                            LanguageId = orc.LanguageId,
                            Status = orc.Status.GetDisplayName(),
                            HasDraft = orc.Versions.Any(x => x.IsDraft),
                            HasPublished = orc.Versions.Any(x => x.IsPublished),
                            ResourceContentStatus = orc.Status,
                        }),
                AssociatedResources = rc.Resource.AssociatedResources.Select(ar => new AssociatedContentResponse
                {
                    // Get the associated resource content for the current content's language or fallback to English
                    ResourceContent = ar.AssociatedResource
                        .ResourceContents
                        .Where(rci => rci.MediaType == ResourceContentMediaType.Text)
                        .OrderByDescending(rci => rci.LanguageId == rc.LanguageId
                            ? 2
                            : rci.LanguageId == Constants.EnglishLanguageId
                                ? 1
                                : 0)
                        .FirstOrDefault(),
                    EnglishLabel = ar.AssociatedResource.EnglishLabel,
                    ParentResourceName = ar.AssociatedResource.ParentResource.DisplayName,
                    MediaTypes = ar.AssociatedResource.ResourceContents.Select(arrc => arrc.MediaType),
                }),
                ProjectEntity =
                    rc.ProjectResourceContents.FirstOrDefault(prc => prc.ResourceContentId == request.Id) == null
                        ? null
                        : rc.ProjectResourceContents.First(prc => prc.ResourceContentId == request.Id).Project,
                HasPublishedVersion = rc.Versions.Any(rcv => rcv.IsPublished),
                HasAdditionalReviewer = rc.Versions.Any(rcv => rcv.AssignedReviewerUser != null && rcv.IsDraft),
            })
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

        if (resourceContent is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        resourceContent.AssociatedResources = resourceContent.AssociatedResources.OrderBy(x => x.EnglishLabel);

        resourceContent.VerseReferences = await dbContext.VerseResources
            .Where(x => x.ResourceId == resourceContent.ResourceId)
            .Select(vr => new VerseReferenceResponse { VerseId = vr.VerseId })
            .ToListAsync(ct);

        resourceContent.PassageReferences = await dbContext.PassageResources
            .Where(x => x.ResourceId == resourceContent.ResourceId)
            .Select(pr => new PassageReferenceResponse
            {
                StartVerseId = pr.Passage.StartVerseId,
                EndVerseId = pr.Passage.EndVerseId,
            })
            .ToListAsync(ct);

        var relevantContentVersion = await dbContext.ResourceContentVersions
            .Where(rcv => rcv.ResourceContentId == request.Id)
            .Select(x => new
            {
                x.Id,
                x.ResourceContentId,
                x.IsDraft,
                x.IsPublished,
                x.Version,
                x.AssignedUserId,
                x.AssignedUser,
                x.Content,
                x.ContentSize,
                x.DisplayName,
                x.WordCount,
                x.ReviewLevel,
                x.Created,
            })
            .OrderBy(rcv => rcv.IsDraft
                ? 0
                : rcv.IsPublished
                    ? 1
                    : 2)
            .ThenByDescending(rcv => rcv.Version)
            .FirstOrDefaultAsync(ct);

        if (relevantContentVersion is null)
        {
            ThrowError("Data integrity issue, no resource content version found.");
        }

        if (relevantContentVersion.IsDraft &&
            relevantContentVersion.AssignedUserId != currentUserId &&
            userService.HasPermission(PermissionName.SendReviewContent) &&
            Constants.ReviewPendingStatuses.Contains(resourceContent.Status))
        {
            resourceContent.CanPullBackToCompanyReview = resourceContent.ProjectEntity?.CompanyLeadUserId == currentUserId ||
                await dbContext.ResourceContentVersionAssignedUserHistory
                    .Where(h => h.ResourceContentVersionId == relevantContentVersion.Id && h.AssignedUserId != null)
                    .OrderByDescending(h => h.Created)
                    .Select(h => h.AssignedUserId == currentUserId)
                    .FirstOrDefaultAsync(ct);
        }

        var snapshots = await dbContext.ResourceContentVersionSnapshots
            .Where(rcvs => rcvs.ResourceContentVersionId == relevantContentVersion.Id)
            .OrderByDescending(rcvs => rcvs.Created)
            .Select(rcvs => new SnapshotResponse
            {
                Id = rcvs.Id,
                Created = rcvs.Created,
                AssignedUserName = rcvs.User == null ? null : $"{rcvs.User.FirstName} {rcvs.User.LastName}",
                Status = rcvs.Status.GetDisplayName(),
            })
            .ToListAsync(ct);

        var versions = await dbContext.ResourceContentVersions
            .Where(rcv => rcv.Id != relevantContentVersion.Id && rcv.ResourceContentId == relevantContentVersion.ResourceContentId)
            .OrderByDescending(rcv => rcv.Created)
            .Select(rcv => new VersionResponse
            {
                Id = rcv.Id,
                Created = rcv.Created,
                Version = rcv.Version,
                IsPublished = rcv.IsPublished,
            })
            .ToListAsync(ct);

        if (resourceContent.Language.Id != 1 &&
            relevantContentVersion.Version > 1 &&
            relevantContentVersion.IsDraft &&
            resourceContent.MediaType == ResourceContentMediaType.Text)
        {
            var firstVersion = versions.FirstOrDefault();
            if (firstVersion is not null)
            {
                var firstVersionSnapshot = await dbContext.ResourceContentVersionSnapshots
                    .Where(rcvs => rcvs.ResourceContentVersionId == firstVersion.Id)
                    .OrderBy(x => x.Created)
                    .Select(x => new SnapshotResponse
                    {
                        Id = x.Id,
                        Created = x.Created,
                        AssignedUserName = null,
                        Status = x.Status.GetDisplayName(),
                    })
                    .FirstOrDefaultAsync(ct);

                if (firstVersionSnapshot is not null)
                {
                    snapshots.Add(firstVersionSnapshot);
                }
            }
        }

        // Can this move into the IsDraft check below?
        resourceContent.MachineTranslations = await dbContext.ResourceContentVersionMachineTranslations
            .Where(x => x.ResourceContentVersionId == relevantContentVersion.Id)
            .Select(mt => new MachineTranslationResponse
            {
                Id = mt.Id,
                ContentIndex = mt.ContentIndex,
                UserId = mt.UserId,
                UserRating = mt.UserRating,
                ImproveClarity = mt.ImproveClarity,
                ImproveConsistency = mt.ImproveConsistency,
                ImproveTone = mt.ImproveTone,
                HadRetranslation = mt.RetranslationReason != null,
                Created = mt.Created,
            })
            .ToListAsync(ct);

        var relatedAudioResourceContentIds = await dbContext.ResourceContents
            .Where(rc => rc.Id != resourceContent.ResourceContentId &&
                rc.ResourceId == resourceContent.ResourceId &&
                rc.LanguageId == resourceContent.Language.Id &&
                rc.MediaType == ResourceContentMediaType.Audio)
            .Select(rc => rc.Id)
            .ToListAsync(ct);

        resourceContent.AudioResources = relatedAudioResourceContentIds
            .Select(rcId => new AudioContentResponse { ContentId = rcId })
            .ToList();

        resourceContent.IsDraft = relevantContentVersion.IsDraft;
        resourceContent.ResourceContentVersionId = relevantContentVersion.Id;
        resourceContent.ResourceContentVersionCreated = relevantContentVersion.Created;
        resourceContent.ContentValue = relevantContentVersion.Content;
        resourceContent.ContentSize = relevantContentVersion.ContentSize;
        resourceContent.DisplayName = relevantContentVersion.DisplayName;
        resourceContent.WordCount = relevantContentVersion.WordCount;
        resourceContent.Snapshots = snapshots;
        resourceContent.Versions = versions;
        resourceContent.ReviewLevel = relevantContentVersion.ReviewLevel;

        if (resourceContent.IsDraft)
        {
            var commentThreads = await dbContext.ResourceContentVersionCommentThreads
                .Include(x => x.CommentThread)
                .ThenInclude(x => x.Comments)
                .ThenInclude(x => x.User)
                .Where(x => x.ResourceContentVersionId == relevantContentVersion.Id)
                .ToListAsync(ct);

            resourceContent.CommentThreads = new CommentThreadsResponse
            {
                ThreadTypeId = relevantContentVersion.Id,
                Threads = commentThreads.Select(x => new ThreadResponse
                    {
                        Id = x.CommentThreadId,
                        Resolved = x.CommentThread.Resolved,
                        Comments = x.CommentThread
                            .Comments
                            .Select(c => new CommentResponse
                            {
                                Id = c.Id,
                                Comment = c.Comment,
                                User = UserDto.FromUserEntity(c.User)!,
                                DateTime = c.Updated,
                            })
                            .ToList(),
                    })
                    .ToList(),
            };

            if (relevantContentVersion.AssignedUser is not null)
            {
                resourceContent.AssignedUser = new UserResponse
                {
                    Id = relevantContentVersion.AssignedUser.Id,
                    Name = $"{relevantContentVersion.AssignedUser.FirstName} {relevantContentVersion.AssignedUser.LastName}",
                    CompanyId = relevantContentVersion.AssignedUser.CompanyId,
                };
            }
        }

        await SendOkAsync(resourceContent, ct);
    }
}