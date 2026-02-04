using Aquifer.Common.Services;
using Aquifer.Common.Utilities;
using Aquifer.Data.Entities;
using FastEndpoints;

namespace Aquifer.API.Endpoints.Resources.Translated.List;

public class Endpoint(IResourceContentSearchService _resourceContentSearchService)
    : Endpoint<Request, IReadOnlyList<Response>>
{
    public override void Configure()
    {
        Get("/resources/translated");
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var verseIdRanges = BibleUtilities.VerseRangesForBookAndChapters(request.BookCode, request.Chapters);

        // search for already translated resources
        var (_, resourceContentSummaries) = await _resourceContentSearchService.SearchAsync(
            ResourceContentSearchIncludeFlags.Project,
            new ResourceContentSearchFilter
            {
                IncludeContentMediaTypes = [ResourceContentMediaType.Text],
                IsPublished = true,
                IsTranslated = true,
                ExcludeActiveProjects = true,
                LanguageId = request.LanguageId,
                ParentResourceId = request.ParentResourceId,
                ResourceEnglishLabelQuery = request.SearchQuery,
                VerseIdRanges = verseIdRanges.Select(vir => new VerseIdRange(vir)).ToList(),
            },
            ResourceContentSearchSortOrder.ParentResourceAndResourceName,
            0,
            null,
            ct);

        var response = resourceContentSummaries
            .Select(rcs => new Response
            {
                ResourceId = rcs.Resource.Id,
                Title = rcs.Resource.EnglishLabel,
                SortOrder = rcs.Resource.SortOrder,
                WordCount = rcs.ResourceContentVersion!.WordCount ?? 0,
            })
            .ToList();

        await SendOkAsync(response, ct);
    }
}