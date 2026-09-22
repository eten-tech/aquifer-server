using System.Net;
using Aquifer.API.Endpoints.Admin.Projects.PreTranslate;
using FastEndpoints;
using FastEndpoints.Testing;

namespace Aquifer.API.IntegrationTests.Endpoints.Admin.Projects.PreTranslate;

/// <summary>
/// Only negative cases are covered here. A successful request publishes a real message to the translate-project-resources queue, which
/// would kick off a real AI translation run on every test pass, so the happy path is verified manually instead.
/// </summary>
public sealed class EndpointTests(App _app) : TestBase<App>
{
    private const int NonExistentProjectId = int.MaxValue;

    [Fact]
    public async Task InvalidRequest_NoApiKey_ShouldReturnUnauthorized()
    {
        var response = await _app.Client.POSTAsync<Endpoint, Request>(
            new Request
            {
                Id = NonExistentProjectId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidRequest_UnauthenticatedRequest_ShouldReturnUnauthorized()
    {
        var response = await _app.AnonymousClient.POSTAsync<Endpoint, Request>(
            new Request
            {
                Id = NonExistentProjectId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(TestUserClient.Editor)]
    [InlineData(TestUserClient.Manager)]
    [InlineData(TestUserClient.Reviewer)]
    [InlineData(TestUserClient.CommunityReviewer)]
    public async Task InvalidRequest_AuthenticatedWithoutThePermission_ShouldReturnForbidden(TestUserClient testUserClient)
    {
        var client = testUserClient switch
        {
            TestUserClient.Editor => _app.EditorClient,
            TestUserClient.Manager => _app.ManagerClient,
            TestUserClient.Reviewer => _app.ReviewerClient,
            TestUserClient.CommunityReviewer => _app.CommunityReviewerClient,
            _ => throw new ArgumentOutOfRangeException(nameof(testUserClient), testUserClient, null),
        };

        var response = await client.POSTAsync<Endpoint, Request>(
            new Request
            {
                Id = NonExistentProjectId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Not covered here: the 404-for-unknown-project and validation-failure cases. Both need a client holding the
    // "requeue-pre-translation:project" permission, and this harness has no admin client because that permission does not exist in Auth0
    // yet. Add an AdminClient to App and the cases below once it does.

    public enum TestUserClient
    {
        Editor,
        Manager,
        Reviewer,
        CommunityReviewer,
    }
}
