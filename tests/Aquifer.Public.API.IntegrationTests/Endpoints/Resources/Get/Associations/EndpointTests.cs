using System.Net;
using Aquifer.Public.API.Endpoints.Resources.Get.Associations;
using FastEndpoints;
using FastEndpoints.Testing;

namespace Aquifer.Public.API.IntegrationTests.Endpoints.Resources.Get.Associations;

public sealed class EndpointTests(App _app) : TestBase<App>
{
    private const int ImageResourceContentId = 303872;
    private const int TextResourceContentId = 1432;

    [Fact]
    public async Task InvalidRequest_NoApiKey_ShouldReturnUnauthorized()
    {
        var (response, result) = await _app.Client.GETAsync<Endpoint, Request, Response>(
            new Request
            {
                ContentId = TextResourceContentId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        result.Should().BeNull();
    }

    [Theory(Skip = "Data dependency - resource 303872 missing passage associations")]
    [InlineData(ImageResourceContentId, true, false)]
    [InlineData(TextResourceContentId, true, true)]
    public async Task ValidRequest_ShouldReturnSuccess(
        int resourceContentId,
        bool expectHasPassageAssociations,
        bool expectHasResourceAssociations)
    {
        var (response, result) = await _app.AnonymousClient.GETAsync<Endpoint, Request, Response>(
            new Request
            {
                ContentId = resourceContentId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();

        // assert that all response data is populated
        if (expectHasPassageAssociations)
        {
            result.PassageAssociations.Should().NotBeNullOrEmpty();
            foreach (var passageAssociation in result.PassageAssociations)
            {
                passageAssociation.StartBookCode.Should().NotBeNullOrEmpty();
                passageAssociation.StartChapter.Should().BeGreaterThan(0);
                passageAssociation.StartVerse.Should().BeGreaterThan(0);

                passageAssociation.EndBookCode.Should().NotBeNullOrEmpty();
                passageAssociation.EndChapter.Should().BeGreaterThan(0);
                passageAssociation.EndVerse.Should().BeGreaterThan(0);
            }
        }
        else
        {
            result.PassageAssociations.Should().BeEmpty();
        }

        if (expectHasResourceAssociations)
        {
            result.ResourceAssociations.Should().NotBeNullOrEmpty();
            foreach (var resourceAssociation in result.ResourceAssociations)
            {
                resourceAssociation.ContentId.Should().BeGreaterThan(0);
                resourceAssociation.DisplayName.Should().NotBeNullOrEmpty();
                resourceAssociation.ReferenceId.Should().BeGreaterThan(0);
            }
        }
        else
        {
            result.ResourceAssociations.Should().BeEmpty();
        }
    }
}