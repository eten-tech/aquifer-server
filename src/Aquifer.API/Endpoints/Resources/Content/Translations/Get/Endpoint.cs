using Aquifer.API.Common;
using Aquifer.Common.Extensions;
using Aquifer.Data;
using Aquifer.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Resources.Content.Translations.Get;

public class Endpoint(AquiferDbContext dbContext) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/resources/content/{Id}/translations");
        Permissions(PermissionName.ReadResources);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var resourceContent = await dbContext.ResourceContents
            .Where(x => x.Id == request.Id)
            .Select(rc => new Response
            {
                ContentTranslations = rc.Resource
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
            })
            .FirstOrDefaultAsync(ct);

        if (resourceContent is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(resourceContent, ct);
    }
}