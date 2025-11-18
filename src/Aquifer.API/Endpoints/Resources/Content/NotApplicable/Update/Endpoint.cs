using Aquifer.API.Common;
using Aquifer.API.Services;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Resources.Content.NotApplicable.Update;

public class Endpoint(AquiferDbContext dbContext, IUserService userService, IResourceHistoryService historyService) : Endpoint<Request>
{
    public override void Configure()
    {
        Post("/resources/content/{ContentId}/not-applicable");
        Permissions(PermissionName.SetStatusTranslationNotApplicable);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var user = await userService.GetUserFromJwtAsync(ct);
        List<ResourceContentStatus> allowedStatuses =
        [
            ResourceContentStatus.TranslationAiDraftComplete,
            ResourceContentStatus.TranslationEditorReview,
            ResourceContentStatus.TranslationCompanyReview,
            ResourceContentStatus.AquiferizeAiDraftComplete,
            ResourceContentStatus.AquiferizeEditorReview,
            ResourceContentStatus.AquiferizeCompanyReview,
        ];

        var content = await dbContext.ResourceContentVersions
            .AsTracking()
            .Include(x => x.ResourceContent)
            .SingleOrDefaultAsync(
                x =>
                    x.ResourceContentId == request.ContentId &&
                    x.IsDraft &&
                    user.Id == x.AssignedUserId &&
                    allowedStatuses.Contains(x.ResourceContent.Status),
                ct);

        if (content is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        content.AssignedUserId = null;
        content.ResourceContent.Status = ResourceContentStatus.TranslationNotApplicable;
        content.ResourceContent.NotApplicableReason = request.NotApplicableReason;

        await historyService.AddAssignedUserHistoryAsync(content, null, user.Id, ct);
        await historyService.AddStatusHistoryAsync(content, ResourceContentStatus.TranslationNotApplicable, user.Id, ct);
        await dbContext.SaveChangesAsync(ct);

        await SendNoContentAsync(ct);
    }
}