using System.Net;
using Aquifer.Data.Entities;
using Aquifer.Well.API.Endpoints.Resources.ParentResources.List;
using FastEndpoints;
using FastEndpoints.Testing;

namespace Aquifer.Well.API.IntegrationTests.Endpoints.ParentResources.List;

public sealed class ListParentResourcesEndpointTests(App _app) : TestBase<App>
{
    [Fact]
    public async Task InvalidRequest_NoApiKey_ShouldReturnUnauthorized()
    {
        var (response, results) =
            await _app.Client.GETAsync<ListParentResourcesEndpoint, IReadOnlyList<ListParentResourcesResponse>>();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        results.Should().BeNull();
    }

    [Fact]
    public async Task ValidRequest_ShouldReturnSuccess()
    {
        var (response, results) =
            await _app.AnonymousClient.GETAsync<ListParentResourcesEndpoint, IReadOnlyList<ListParentResourcesResponse>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results.Should().NotBeNullOrEmpty();

        // assert that response data is populated
        foreach (var result in results)
        {
            result.DisplayName.Should().NotBeNullOrEmpty();
            result.ResourceType.Should().NotBe(ResourceType.None);
            result.ShortName.Should().NotBeNullOrEmpty();

            result.Localizations.Should().NotBeEmpty();
            foreach (var localization in result.Localizations)
            {
                localization.DisplayName.Should().NotBeNullOrEmpty();
                localization.LanguageId.Should().BeGreaterThan(0);
            }

            result.LicenseInfo.Copyright.Holder.Name.Should().NotBeNullOrEmpty();
            result.LicenseInfo.Copyright.Holder.Url.Should().NotBeNullOrEmpty();
            result.LicenseInfo.Title.Should().NotBeNullOrEmpty();
            foreach (var license in result.LicenseInfo.Licenses)
            {
                license.Eng.Name.Should().NotBeNullOrEmpty();
                license.Eng.Url.Should().NotBeNullOrEmpty();
            }
        }
    }
}