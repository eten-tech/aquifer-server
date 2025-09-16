using Aquifer.API.Common;
using Aquifer.Common;
using Aquifer.Data;
using Aquifer.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Resources.Content.CitedBy.Get;

public class Endpoint(AquiferDbContext dbContext) : Endpoint<Request, Response>

{
    public override void Configure()
    {
        Get("/resources/content/{Id}/cited-by");
        Permissions(PermissionName.ReadResources);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var resourceContent = dbContext.ResourceContents.Where(rc => rc.Id == request.Id).FirstOrDefault();
        
        if (resourceContent is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var citedByContent = await dbContext.AssociatedResources
            .Where(ar => ar.AssociatedResourceId == resourceContent.ResourceId)
            .Select(ar => new AssociatedContentResponse
            {
                ResourceContent = ar.Resource
                    .ResourceContents
                    .Where(rci => rci.MediaType == ResourceContentMediaType.Text)
                    .OrderByDescending(rci => rci.LanguageId == resourceContent.LanguageId
                        ? 2
                        : rci.LanguageId == Constants.EnglishLanguageId
                            ? 1
                            : 0)
                    .FirstOrDefault(),
                ParentResourceName = ar.Resource.ParentResource.DisplayName,
                EnglishLabel = ar.Resource.EnglishLabel,
                MediaTypes = ar.Resource.ResourceContents.Select(arrci => arrci.MediaType),
            })
            .ToListAsync(ct);

        await SendOkAsync(new Response { CitedByContent = citedByContent }, ct);
    }
}