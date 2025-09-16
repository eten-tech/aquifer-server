using System.Data.Common;
using Aquifer.Common.Services.Caching;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;
using Aquifer.Public.API.Helpers;
using Dapper;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Public.API.Endpoints.Resources.Collections.Get;

public sealed class Endpoint(AquiferDbReadOnlyContext _dbContext, ICachingLanguageService _cachingLanguageService)
    : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/resources/collections/{code}");
        Options(EndpointHelpers.UnauthenticatedServerCacheInSeconds(EndpointHelpers.OneHourInSeconds));
        Description(d => d.WithTags("Resources/Collections").ProducesProblemFE().ProducesProblemFE(404));
        Summary(s =>
        {
            s.Summary = "Get a resource collection with language localization data for the given collection code.";
            s.Description =
                "Returns the resource collection matching the collection code including the count of resource items in each available language.";
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var (invalidLanguageIds, invalidLanguageCodes, validLanguageIds) = await _cachingLanguageService.ValidateLanguageIdsOrCodesAsync(
            req.LanguageIds,
            req.LanguageCodes,
            false,
            ct);

        if (invalidLanguageIds is not [])
        {
            ThrowError(r => r.LanguageIds, $"Invalid 'Language Ids': \"{string.Join("\", \"", invalidLanguageIds)}\"");
        }

        if (invalidLanguageCodes is not [])
        {
            ThrowError(r => r.LanguageCodes, $"Invalid 'Language Codes': \"{string.Join("\", \"", invalidLanguageCodes)}\"");
        }

        var dbConnection = _dbContext.Database.GetDbConnection();

        var parentResource = await GetParentResourceAsync(dbConnection, req.Code, ct);
        if (parentResource is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var localizations = await GetParentResourceLocalizationWithResourceContentCountAsync(
            dbConnection,
            parentResource.Id,
            validLanguageIds,
            ct);

        var languageCodeByIdMap = await _cachingLanguageService.GetLanguageCodeByIdMapAsync(ct);

        var response = new Response
        {
            Code = parentResource.Code,
            DisplayName = parentResource.DisplayName,
            ShortName = parentResource.ShortName,
            ResourceType = parentResource.ResourceType,
            SliCategory = parentResource.SliCategory,
            SliLevel = parentResource.SliLevel,
            LicenseInfo = JsonUtilities.DefaultDeserialize<ParentResourceLicenseInfoSchema>(parentResource.LicenseInfo),
            AvailableLanguages = localizations.Select(l => new AvailableLanguageResponse
                {
                    LanguageId = l.LanguageId,
                    LanguageCode = languageCodeByIdMap[l.LanguageId],
                    DisplayName = l.DisplayName,
                    ResourceItemCount = l.ResourceCount,
                })
                .ToList(),
        };

        await SendOkAsync(response, ct);
    }

    private static async Task<ParentResource?> GetParentResourceAsync(DbConnection dbConnection, string code, CancellationToken ct)
    {
        const string query = """
            SELECT
                pr.Id,
                pr.Code,
                pr.DisplayName,
                pr.ShortName,
                pr.ResourceType,
                pr.LicenseInfo,
                pr.SliCategory,
                pr.SliLevel
            FROM
                ParentResources pr
            WHERE
                pr.Code = @code AND
                pr.[Enabled] = 1;
            """;

        return await dbConnection.QuerySingleOrDefaultWithRetriesAsync<ParentResource>(
            query,
            new
            {
                code,
            },
            cancellationToken: ct);
    }

    private static async Task<IReadOnlyList<ParentResourceLocalizationWithResourceContentCount>>
        GetParentResourceLocalizationWithResourceContentCountAsync(
            DbConnection dbConnection,
            int parentResourceId,
            IReadOnlyList<int>? languageIds,
            CancellationToken ct)
    {
        var parameters = new DynamicParameters(new { parentResourceId });

        string? parentResourceLocalizationsWhereClause = null;
        string? resourceContentCountWhereClause = null;
        if (languageIds is not null or [])
        {
            parameters.Add(nameof(languageIds), languageIds);
            parentResourceLocalizationsWhereClause = """
            WHERE
                x.LanguageId IN @languageIds
            """;
            resourceContentCountWhereClause = "rc.LanguageId IN @languageIds AND";
        }

        // there is no English data in the ParentResourceLocalizations table so fetch it from the main ParentResources table
        var query = $"""
            SELECT
                x.LanguageId,
                x.DisplayName
            FROM
            (
                SELECT
                    1 AS LanguageId,
                    pr.DisplayName
                FROM
                    ParentResources pr
                WHERE
                    pr.Id = @parentResourceId

                UNION

                SELECT
                    prl.LanguageId,
                    prl.DisplayName
                FROM
                    ParentResourceLocalizations prl
                WHERE
                    prl.ParentResourceId = @parentResourceId
            ) x
            {parentResourceLocalizationsWhereClause}
            ORDER BY
                x.LanguageId;

            SELECT
                rc.LanguageId,
                COUNT(rcv.Id) AS ResourceCount
            FROM Resources r
                JOIN ResourceContents rc ON rc.ResourceId = r.Id
                JOIN ResourceContentVersions rcv ON rcv.ResourceContentId = rc.Id
            WHERE
                r.ParentResourceId = @parentResourceId AND
                {resourceContentCountWhereClause}
                rcv.IsPublished = 1
            GROUP BY
                rc.LanguageId;
            """;

        // querying separately and joining in memory saves significant time over trying to join in the DB
        await using var reader = await dbConnection.QueryMultipleWithRetriesAsync(query, parameters, cancellationToken: ct);

#pragma warning disable VSTHRD103 // using the non-async Read() method is correct because I/O was already awaited above in QueryMultipleAsync()
        var parentResourceLocalizations = reader.Read<ParentResourceLocalization>().ToList();
        var resourceContentCounts = reader.Read<ResourceContentCount>().ToList();
#pragma warning restore VSTHRD103

        // Note that there may be more ParentResourceLocalization entries than counts because we translate the parent resource names
        // before resource contents begin translation.  If this happens, then due to the Join() call here all
        // ParentResourceLocalizations without any translated resource contents for a given language will be omitted.
        return parentResourceLocalizations.Join(
                resourceContentCounts,
                prl => prl.LanguageId,
                rcc => rcc.LanguageId,
                (prl, rcc) => new ParentResourceLocalizationWithResourceContentCount(prl.LanguageId, prl.DisplayName, rcc.ResourceCount))
            .ToList();
    }

    private sealed record ParentResource(
        int Id,
        string Code,
        string DisplayName,
        string ShortName,
        ResourceType ResourceType,
        string LicenseInfo,
        string? SliCategory,
        int? SliLevel);

    private sealed record ParentResourceLocalization(int LanguageId, string DisplayName);

    private sealed record ResourceContentCount(int LanguageId, int ResourceCount);

    private sealed record ParentResourceLocalizationWithResourceContentCount(int LanguageId, string DisplayName, int ResourceCount);
}