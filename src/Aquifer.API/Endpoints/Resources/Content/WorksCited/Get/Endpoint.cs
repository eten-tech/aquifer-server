using Aquifer.API.Common;
using Aquifer.Common;
using Aquifer.Data;
using Aquifer.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Resources.Content.WorksCited.Get;

public class Endpoint(AquiferDbContext dbContext) : Endpoint<Request, Response>

{
    public override void Configure()
    {
        Get("/resources/content/{Id}/works-cited");
        Permissions(PermissionName.ReadResources);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var resourceContent = await dbContext.ResourceContents
            .Where(x => x.Id == request.Id)
            .Select(rc => new Response
            {
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
            })
            .FirstOrDefaultAsync(ct);

        if (resourceContent is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        resourceContent.AssociatedResources = resourceContent.AssociatedResources.OrderBy(x => x.EnglishLabel);

        await SendOkAsync(resourceContent, ct);
    }
}