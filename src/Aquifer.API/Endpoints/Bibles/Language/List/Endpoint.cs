using Aquifer.API.Helpers;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Schemas;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Bibles.Language.List;

public class Endpoint(AquiferDbContext dbContext) : Endpoint<Request, List<Response>>
{
    public override void Configure()
    {
        Get("/bibles/language/{LanguageId}");
        ResponseCache(EndpointHelpers.OneHourInSeconds);
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var bibles = await dbContext.Bibles
            .Where(x => x.Enabled && x.LanguageId == request.LanguageId && x.RestrictedLicense == request.RestrictedLicense)
            .Select(bible => new Response
            {
                Name = bible.Name,
                Abbreviation = bible.Abbreviation,
                LicenseInfo = JsonUtilities.DefaultDeserialize<BibleLicenseInfoSchema>(bible.LicenseInfo),
                Id = bible.Id,
                LanguageId = bible.LanguageId,
                RestrictedLicense = bible.RestrictedLicense,
                Books = bible.BibleBookContents
                    .OrderBy(bbc => bbc.BookId)
                    .Select(bbc => new BibleBookMetadataResponse
                    {
                        BookCode = BibleBookCodeUtilities.CodeFromId(bbc.BookId),
                        DisplayName = bbc.DisplayName,
                        TextSize = bbc.TextSize,
                        AudioSize = bbc.AudioSize,
                        ChapterCount = bbc.ChapterCount,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);

        await SendOkAsync(bibles, ct);
    }
}