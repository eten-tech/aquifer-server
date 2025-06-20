using System.Net;
using Aquifer.Well.API.Endpoints.Resources.ParentResources.Passages.Get;
using FastEndpoints;
using FastEndpoints.Testing;

namespace Aquifer.Well.API.IntegrationTests.Endpoints.ParentResources.Passages.Get;

public sealed class GetPassagesForParentResourceEndpointTests(App _app) : TestBase<App>
{
    public static TheoryData<GetPassagesForParentResourceRequest> GetValidRequestData =>
    [
        new GetPassagesForParentResourceRequest
        {
            ParentResourceId = 2,
            LanguageId = 1,
        },
        new GetPassagesForParentResourceRequest
        {
            ParentResourceId = 5,
            LanguageId = 12,
        },
    ];

    [Theory]
    [MemberData(nameof(GetValidRequestData))]
    public async Task InvalidRequest_NoApiKey_ShouldReturnUnauthorized(GetPassagesForParentResourceRequest request)
    {
        var (response, results) = await _app.Client
            .GETAsync<GetPassagesForParentResourceEndpoint,
                GetPassagesForParentResourceRequest,
                IReadOnlyList<GetPassagesForParentResourceResponse>>(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        results.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(GetValidRequestData))]
    public async Task ValidRequest_ShouldReturnSuccess(GetPassagesForParentResourceRequest request)
    {
        var (response, results) =
            await _app.AnonymousClient
                .GETAsync<GetPassagesForParentResourceEndpoint,
                    GetPassagesForParentResourceRequest,
                    IReadOnlyList<GetPassagesForParentResourceResponse>>(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results.Should().NotBeNullOrEmpty();

        results.Count.Should().BeGreaterThan(0);
        foreach (var result in results)
        {
            // TODO: Remove invalid verse IDs from the DB
            //result.StartVerseId.Should().BeGreaterThan(1001000000);
            //result.EndVerseId.Should().BeGreaterThan(1001000000);

            result.StartVerseId.Should().BeLessThanOrEqualTo(1087999999);
            result.EndVerseId.Should().BeLessThanOrEqualTo(1087999999);

            result.EndVerseId.Should().BeGreaterThanOrEqualTo(result.StartVerseId);
        }
    }
}