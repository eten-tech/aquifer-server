using System.Data.Common;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Well.API.Helpers;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.List;

public class ListParentResourcesEndpoint(AquiferDbContext _dbContext)
    : EndpointWithoutRequest<IReadOnlyList<ListParentResourcesResponse>>
{
    public override void Configure()
    {
        Get("/resources/parent-resources");
        ResponseCache(EndpointHelpers.OneHourInSeconds);
        Options(EndpointHelpers.ServerCacheInSeconds(EndpointHelpers.TenMinutesInSeconds));

        Description(d => d.ProducesProblemFE());
        Summary(s =>
        {
            s.Summary = "Return parent resources list.";
            s.Description = "Return a list of parent resources and localization information.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dbConnection = _dbContext.Database.GetDbConnection();

        var parentResources = await GetParentResourcesAsync(dbConnection, ct);
        var localizationsByParentResourceId = (await GetParentResourceLocalizationsAsync(dbConnection, ct))
            .ToLookup(prl => prl.ParentResourceId);

        var response = parentResources
            .Select(pr => new ListParentResourcesResponse
            {
                Id = pr.Id,
                Code = pr.Code,
                DisplayName = pr.DisplayName,
                ShortName = pr.ShortName,
                ResourceType = pr.ResourceType,
                LicenseInfo = JsonUtilities.DefaultDeserialize<ResourceLicenseInfo>(pr.LicenseInfo),
                Localizations = localizationsByParentResourceId[pr.Id]
                    .Select(l => new ParentResourcesLocalization
                    {
                        LanguageId = l.LanguageId,
                        DisplayName = l.DisplayName,
                    })
                    .ToList(),
            })
            .ToList();

        await SendOkAsync(response, ct);
    }

    private static async Task<IReadOnlyList<ParentResource>> GetParentResourcesAsync(DbConnection dbConnection, CancellationToken ct)
    {
        const string query = """
            SELECT
                pr.Id,
                pr.Code,
                pr.DisplayName,
                pr.ShortName,
                pr.ResourceType,
                pr.LicenseInfo
            FROM
                ParentResources pr
            WHERE
                pr.[Enabled] = 1;
            """;

        return (await dbConnection.QueryWithRetriesAsync<ParentResource>(
                query,
                cancellationToken: ct))
            .ToList();
    }

    private static async Task<IReadOnlyList<ParentResourceLocalization>>
        GetParentResourceLocalizationsAsync(
            DbConnection dbConnection,
            CancellationToken ct)
    {
        // there is no English data in the ParentResourceLocalizations table so fetch it from the main ParentResources table
        const string query = """
            SELECT
                x.ParentResourceId,
                x.LanguageId,
                x.DisplayName
            FROM
            (
                SELECT
                    pr.Id AS ParentResourceId,
                    1 AS LanguageId,
                    pr.DisplayName
                FROM
                    ParentResources pr

                UNION

                SELECT
                    prl.ParentResourceId,
                    prl.LanguageId,
                    prl.DisplayName
                FROM
                    ParentResourceLocalizations prl
            ) x
            ORDER BY
                x.ParentResourceId,
                x.LanguageId;
            """;

        // querying separately and joining in memory saves significant time over trying to join in the DB
        return (await dbConnection.QueryWithRetriesAsync<ParentResourceLocalization>(
                query,
                cancellationToken: ct))
            .ToList();
    }

    private sealed record ParentResource(
        int Id,
        string Code,
        string DisplayName,
        string ShortName,
        ResourceType ResourceType,
        string LicenseInfo);

    private sealed record ParentResourceLocalization(
        int ParentResourceId,
        int LanguageId,
        string DisplayName);
}