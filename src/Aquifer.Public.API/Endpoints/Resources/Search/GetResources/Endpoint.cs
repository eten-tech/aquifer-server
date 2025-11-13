using Aquifer.Common.Services.Caching;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Public.API.Helpers;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Public.API.Endpoints.Resources.Search.GetResources;

public class Endpoint(AquiferDbReadOnlyContext dbContext, ICachingLanguageService cachingLanguageService) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/resources/search");
        Options(EndpointHelpers.UnauthenticatedServerCacheInSeconds(EndpointHelpers.TenMinutesInSeconds));
        Description(d => d
            .WithTags("Resources")
            .ProducesProblemFE());
        Summary(s =>
        {
            s.Summary = "Search resources by keyword query, passage, resource type or collection code, or a combination of the above.";
            s.Description =
                "For a given query, language, and content type, search for matching resources. Can also search by passage, resource type, or resource collection code.";
        });
    }

    public override void OnValidationFailed()
    {
        var resourceTypeFailure = ValidationFailures.FirstOrDefault(x => x.PropertyName.ToLower() == "resourcetype");
        if (resourceTypeFailure is not null)
        {
            var validValues = string.Join(", ", Enum.GetValues(typeof(ResourceType)).Cast<ResourceType>().ToList());
            var errorMessage = $"{resourceTypeFailure.ErrorMessage} Valid values are {validValues}";
            resourceTypeFailure.ErrorMessage = errorMessage;
        }

        base.OnValidationFailed();
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        var query = await GetQueryAsync(req, ct);
        var totalCount = await GetTotalResourceCountAsync(req, query, ct);

        if (totalCount == 0)
        {
            await SendOkAsync(new Response(), ct);
            return;
        }

        var items = await GetResourcesAsync(req, query, ct);
        var response = new Response
        {
            TotalItemCount = totalCount,
            ReturnedItemCount = items.Count,
            Offset = req.Offset,
            Items = items,
        };

        await SendOkAsync(response, ct);
    }

    private async Task<IQueryable<ResourceContentVersionEntity>> GetQueryAsync(Request req, CancellationToken ct)
    {
        var languageId = req.LanguageCode is not null
            ? await cachingLanguageService.GetLanguageIdAsync(req.LanguageCode, ct) ?? 0
            : req.LanguageId;

        var (startVerseId, endVerseId) = req.BookCode is null
            ? ((int?)null, (int?)null)
            : BibleUtilities.GetVerseIds(req.BookCode, req.StartChapter, req.EndChapter, req.StartVerse, req.EndVerse);

        return dbContext.ResourceContentVersions
            .Where(x =>
                x.IsPublished &&
                x.ResourceContent.Resource.ParentResource.Enabled &&
                (req.Query == null || x.DisplayName.Contains(req.Query) || x.ResourceContent.Resource.EnglishLabel.Contains(req.Query)) &&
                (startVerseId == null ||
                    x.ResourceContent.Resource.VerseResources.Any(vr =>
                        vr.VerseId >= startVerseId && vr.VerseId <= endVerseId) ||
                    x.ResourceContent.Resource.PassageResources.Any(pr =>
                        (pr.Passage.StartVerseId >= startVerseId && pr.Passage.StartVerseId <= endVerseId) ||
                        (pr.Passage.EndVerseId >= startVerseId && pr.Passage.EndVerseId <= endVerseId) ||
                        (pr.Passage.StartVerseId <= startVerseId && pr.Passage.EndVerseId >= endVerseId))) &&
                (req.ResourceType == default || x.ResourceContent.Resource.ParentResource.ResourceType == req.ResourceType) &&
                (req.ResourceCollectionCode == null ||
                    x.ResourceContent.Resource.ParentResource.Code.ToLower() == req.ResourceCollectionCode.ToLower()) &&
                x.ResourceContent.LanguageId == languageId);
    }

    private async Task<int> GetTotalResourceCountAsync(Request req, IQueryable<ResourceContentVersionEntity> query, CancellationToken ct)
    {
        var totalCount = await query.CountAsync(ct);
        if (req.Offset >= totalCount && req.Offset != 0)
        {
            ThrowError($"Offset of {req.Offset} equals or exceeds total of {totalCount}");
        }

        return totalCount;
    }

    private async Task<List<ResponseContent>> GetResourcesAsync(
        Request req,
        IQueryable<ResourceContentVersionEntity> query,
        CancellationToken ct)
    {
        var languageCodeByIdMap = await cachingLanguageService.GetLanguageCodeByIdMapAsync(ct);

        var resources = await query
            .OrderBy(x => x.ResourceContent.Resource.Id)
            .Skip(req.Offset)
            .Take(req.Limit)
            .Select(x => new ResponseContent
            {
                Id = x.ResourceContent.Id,
                Name = x.ResourceContent.Resource.EnglishLabel,
                LocalizedName = x.DisplayName,
                ReviewLevel = x.ReviewLevel,
                LanguageCode = languageCodeByIdMap[x.ResourceContent.LanguageId],
                MediaType = x.ResourceContent.MediaType,
                Grouping = new ResourceTypeMetadata
                {
                    Name = x.ResourceContent.Resource.ParentResource.DisplayName,
                    CollectionTitle = x.ResourceContent.Resource.ParentResource.DisplayName,
                    CollectionCode = x.ResourceContent.Resource.ParentResource.Code,
                    Type = x.ResourceContent.Resource.ParentResource.ResourceType,
                },
            })
            .ToListAsync(ct);

        return resources;
    }
}