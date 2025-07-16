using System.Data.Common;
using System.Net;
using Aquifer.Common.Services.Caching;
using Aquifer.Data;
using Aquifer.Well.API.Helpers;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.Passages.Get;

public class GetPassagesForParentResourceEndpoint(AquiferDbContext _dbContext, ICachingLanguageService _cachingLanguageService)
    : Endpoint<GetPassagesForParentResourceRequest, IReadOnlyList<GetPassagesForParentResourceResponse>>
{
    public override void Configure()
    {
        Get("/resources/parent-resources/{ParentResourceId}/passages");
        ResponseCache(EndpointHelpers.OneHourInSeconds);
        Options(EndpointHelpers.ServerCacheInSeconds(EndpointHelpers.TenMinutesInSeconds));

        Description(d => d
            .ProducesProblemFE()
            .ProducesProblemFE((int)HttpStatusCode.NotFound));
        Summary(s =>
        {
            s.Summary = "Return parent resources list.";
            s.Description = "Return a list of parent resources and localization information.";
        });
    }

    public override async Task HandleAsync(GetPassagesForParentResourceRequest request, CancellationToken ct)
    {
        if (await _dbContext.ParentResources.FirstOrDefaultAsync(pr => pr.Id == request.ParentResourceId, ct) is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (await _cachingLanguageService.GetLanguageAsync(request.LanguageId, ct) is null)
        {
            ThrowError(x => x.LanguageId, $"Invalid {nameof(GetPassagesForParentResourceRequest.LanguageId)}: {request.LanguageId}");
        }

        var dbConnection = _dbContext.Database.GetDbConnection();

        var passages = await GetPassagesAsync(dbConnection, request.ParentResourceId, request.LanguageId, ct);

        var response = passages
            .Select(x => new GetPassagesForParentResourceResponse
            {
                StartVerseId = x.StartVerseId,
                EndVerseId = x.EndVerseId,
            })
            .ToList();

        await SendOkAsync(response, ct);
    }

    private static async Task<IReadOnlyList<Passage>> GetPassagesAsync(
        DbConnection dbConnection,
        int parentResourceId,
        int languageId,
        CancellationToken ct)
    {
        // TODO use exists instead and compare performance
        const string query = """
            SELECT
                x.StartVerseId,
                x.EndVerseId
            FROM
            (
                SELECT
                    p.StartVerseId,
                    p.EndVerseId
                FROM dbo.PassageResources pr
                    JOIN dbo.Passages p ON pr.PassageId = p.Id
                    JOIN dbo.Resources r ON pr.ResourceId = r.Id
                    JOIN dbo.ResourceContents rc ON r.Id = rc.ResourceId
                WHERE
                    r.ParentResourceId = @parentResourceId AND
                    rc.LanguageId = @languageId

                UNION

                SELECT
                    vr.VerseId AS StartVerseId,
                    vr.VerseId AS EndVerseId
                FROM dbo.VerseResources vr
                    JOIN dbo.Resources r ON vr.ResourceId = r.Id
                    JOIN dbo.ResourceContents rc ON r.Id = rc.ResourceId
                WHERE
                    r.ParentResourceId = @parentResourceId AND
                    rc.LanguageId = @languageId
            ) x
            ORDER BY
                x.StartVerseId,
                x.EndVerseId
            """;

        return (await dbConnection.QueryWithRetriesAsync<Passage>(
                query,
                new
                {
                    parentResourceId,
                    languageId,
                },
                cancellationToken: ct))
            .ToList();
    }

    private sealed record Passage(
        int StartVerseId,
        int EndVerseId);
}