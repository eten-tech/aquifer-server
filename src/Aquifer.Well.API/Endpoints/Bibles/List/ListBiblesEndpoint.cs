using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Schemas;
using Aquifer.Well.API.Helpers;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Well.API.Endpoints.Bibles.List;

public class ListBiblesEndpoint(AquiferDbReadOnlyContext dbContext) : Endpoint<ListBiblesRequest, IReadOnlyList<ListBiblesResponse>>
{
    public override void Configure()
    {
        Get("/bibles");
        Options(EndpointHelpers.ServerCacheInSeconds(EndpointHelpers.OneHourInSeconds));
        Description(d => d.ProducesProblemFE());
        Summary(s =>
        {
            s.Summary = "Get a list of Bibles.";
            s.Description =
                "For a given optional language id, returns the Bibles in the system including language information, abbreviation codes, whether or not the Bible supports a Greek alignment, and license information.";
        });
    }

    public override async Task HandleAsync(ListBiblesRequest req, CancellationToken ct)
    {
        var bibles = await dbContext.Bibles
            .Where(b => b.Enabled &&
                (!req.LanguageId.HasValue || b.LanguageId == req.LanguageId) &&
                (!req.IsLanguageDefault.HasValue || b.LanguageDefault == req.IsLanguageDefault))
            .Select(bible => new ListBiblesResponse
            {
                Name = bible.Name,
                Abbreviation = bible.Abbreviation,
                Id = bible.Id,
                LicenseInfo = JsonUtilities.DefaultDeserialize<BibleLicenseInfoSchema>(bible.LicenseInfo),
                LanguageId = bible.LanguageId,
                IsLanguageDefault = bible.LanguageDefault,
                HasAudio = bible.BibleBookContents.Any(bbc => bbc.AudioUrls != null),
                HasGreekAlignment = bible.GreekAlignment,
                ContentIteration = bible.ContentIteration,
            })
            .ToListAsync(ct);

        await SendOkAsync(bibles, ct);
    }
}