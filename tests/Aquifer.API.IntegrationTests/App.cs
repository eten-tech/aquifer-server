using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Aquifer.API.Endpoints.Users.Create;
using Aquifer.API.Endpoints.Users.List;
using Aquifer.API.Services;
using Aquifer.Common.Clients;
using Aquifer.Common.Services;
using Aquifer.Data;
using Aquifer.Data.Entities;
using FastEndpoints;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Endpoint = Aquifer.API.Endpoints.Users.List.Endpoint;

namespace Aquifer.API.IntegrationTests;

/// <summary>
/// This is a FastEndpoints <see cref="App" /> that sits on top of a <see cref="WebApplicationFactory{TEntryPoint}" />
/// which allows for in-memory web requests to be sent and received without actual network traffic.
/// Details:
/// * https://fast-endpoints.com/docs/integration-unit-testing
/// * https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
/// This class configures both the internal API (so it can be spun up in-memory to test against) *and* this integration test project, each
/// of which use separate configuration files. The internal API is configured via the override methods below.  The integration test
/// configuration is only used for creating/authenticating test users.
/// </summary>
public sealed class App : AppFixture<Program>
{
    private const int AquiferApiIntegrationTestsCompanyId = 26;

    public HttpClient AnonymousClient { get; private set; } = null!;
    public HttpClient PublisherClient { get; private set; } = null!;
    public HttpClient ManagerClient { get; private set; } = null!;
    public HttpClient EditorClient { get; private set; } = null!;
    public HttpClient ReviewerClient { get; private set; } = null!;
    public HttpClient CommunityReviewerClient { get; private set; } = null!;

    /// <summary>
    /// Only use this <see cref="IHost" />> for integration test specific configuration outside the internal API's config.
    /// </summary>
    private IHost Host { get; set; } = null!;

    /// <summary>
    /// The app is configured in <see cref="Program" /> before this method is called.
    /// Only use this method to override or extend existing host configuration.
    /// </summary>
    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        // The environment must be explicitly set as it will not default to "Development".
        // On the build server this environment variable should be explicitly populated.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            builder.UseEnvironment(Environments.Development);
        }
    }

    /// <summary>
    /// The service registrations in <see cref="Program" /> run before this method is called.
    /// Only use this method to add additional services or to override existing registrations from the API.
    /// </summary>
    protected override void ConfigureServices(IServiceCollection services)
    {
        // Turn off API output caching for testing.  See https://github.com/FastEndpoints/FastEndpoints/issues/892 for reasoning.
        services.AddSingleton<IOutputCacheStore, DevNullOutputCacheStore>();
    }

    /// <summary>
    /// Configure Clients here.
    /// Various authenticated client with different roles/permissions are needed.
    /// </summary>
    protected override async ValueTask SetupAsync()
    {
        try
        {
            InitializeIntegrationTestAppHost();

            // Get an auth service using the API's configuration (NOT the integration test project's configuration) in order to
            // use the management API to set the test user password.
            var apiScopeFactory = Services.GetRequiredService<IServiceScopeFactory>();
            using var apiSetupScope = apiScopeFactory.CreateScope();
            var apiAuth0Service = apiSetupScope.ServiceProvider.GetRequiredService<IAuth0Service>();

            // Get an auth service using the Integration Test project's configuration (NOT the API's configuration) in order to
            // create/authenticate users using auth0 settings which allow passing a password directly for authentication.
            var integrationTestScopeFactory = Host.Services.GetRequiredService<IServiceScopeFactory>();
            using var integrationTestSetupScope = integrationTestScopeFactory.CreateScope();

            var integrationTestConfiguration =
                integrationTestSetupScope.ServiceProvider.GetRequiredService<IOptions<ConfigurationOptions>>().Value;
            var integrationTestAuth0Service = integrationTestSetupScope.ServiceProvider.GetRequiredService<IAuth0Service>();
            var integrationTestUserSettings =
                integrationTestSetupScope.ServiceProvider.GetRequiredService<ConfigurationOptions.UserSettings>();
            var integrationTestAquiferDbContext = integrationTestSetupScope.ServiceProvider.GetRequiredService<AquiferDbContext>();

            var publisherBearerToken = await integrationTestAuth0Service.GetAccessTokenUsingResourceOwnerPasswordFlowAsync(
                GetTestUserEmail(UserRole.Publisher),
                integrationTestUserSettings.TestUserPassword,
                CancellationToken.None);

            AnonymousClient = CreateClient(integrationTestConfiguration.InternalApiKey, shouldBypassCaching: true);

            // Note: The Publisher client user must be manually created so that we have a user to create the other test users.
            PublisherClient = CreateClient(integrationTestConfiguration.InternalApiKey, publisherBearerToken, true);

            var testUserBearerTokenByRoleMap = await CreateTestUsersAsync(
                PublisherClient,
                integrationTestAquiferDbContext,
                apiAuth0Service,
                integrationTestAuth0Service,
                [UserRole.Manager, UserRole.Editor, UserRole.Reviewer, UserRole.CommunityReviewer],
                integrationTestUserSettings.TestUserPassword,
                CancellationToken.None);

            ManagerClient = CreateClient(
                integrationTestConfiguration.InternalApiKey,
                testUserBearerTokenByRoleMap[UserRole.Manager],
                true);
            EditorClient = CreateClient(
                integrationTestConfiguration.InternalApiKey,
                testUserBearerTokenByRoleMap[UserRole.Editor],
                true);
            ReviewerClient = CreateClient(
                integrationTestConfiguration.InternalApiKey,
                testUserBearerTokenByRoleMap[UserRole.Reviewer],
                true);
            CommunityReviewerClient = CreateClient(
                integrationTestConfiguration.InternalApiKey,
                testUserBearerTokenByRoleMap[UserRole.CommunityReviewer],
                true);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    protected override ValueTask TearDownAsync()
    {
        Host.Dispose();

        AnonymousClient.Dispose();
        PublisherClient.Dispose();
        ManagerClient.Dispose();
        EditorClient.Dispose();
        ReviewerClient.Dispose();
        CommunityReviewerClient.Dispose();

        return ValueTask.CompletedTask;
    }

    private HttpClient CreateClient(string? apiKey = null, string? bearerToken = null, bool shouldBypassCaching = false)
    {
        var client = CreateClient(new ClientOptions { BypassCaching = shouldBypassCaching });

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        if (bearerToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return client;
    }

    private static string GetTestUserEmail(UserRole testUserRole)
    {
        return $"integration.test.user+{testUserRole.ToString().ToLower()}@example.com";
    }

    /// <summary>
    /// Test users are all in the "Aquifer.API.IntegrationTests" company.
    /// A PublisherClient must already be set up before calling this method as it will be used to create any missing users.
    /// If a test user for a given role already exists then the existing user will be used instead of creating a new one.
    /// </summary>
    /// <returns>A map of bearer token for the created/existing user by role.</returns>
    private static async Task<ReadOnlyDictionary<UserRole, string>> CreateTestUsersAsync(
        HttpClient publisherClient,
        AquiferDbContext dbContext,
        IAuth0Service apiAuth0Service,
        IAuth0Service integrationTestingAuth0Service,
        IReadOnlyList<UserRole> testUserRoles,
        string testUserPassword,
        CancellationToken ct)
    {
        var testUserBearerTokenByRoleMap = new Dictionary<UserRole, string>();

        // find existing users using API endpoint
        var getAllUsersResponse = await publisherClient.GETAsync<Endpoint, IReadOnlyList<Response>>();

        getAllUsersResponse.Response.EnsureSuccessStatusCode();

        var usersForIntegrationTestCompanyByEmailMap = getAllUsersResponse.Result
            .Where(x => x.Company.Id == AquiferApiIntegrationTestsCompanyId)
            .ToDictionary(u => u.Email);

        foreach (var testUserRole in testUserRoles)
        {
            var testUserEmail = GetTestUserEmail(testUserRole);

            var testUser = usersForIntegrationTestCompanyByEmailMap.GetValueOrDefault(testUserEmail);
            if (testUser is null)
            {
                await CreateTestUserAsync(
                    apiAuth0Service,
                    dbContext,
                    publisherClient,
                    testUserEmail,
                    testUserPassword,
                    "Integration Test",
                    $"User ({testUserRole})",
                    testUserRole,
                    ct);
            }

            // get user's bearer token using username/password (all integration test users share the same password)
            var testUserBearerToken = await integrationTestingAuth0Service.GetAccessTokenUsingResourceOwnerPasswordFlowAsync(
                testUserEmail,
                testUserPassword,
                CancellationToken.None);

            testUserBearerTokenByRoleMap[testUserRole] = testUserBearerToken;
        }

        return testUserBearerTokenByRoleMap.AsReadOnly();
    }

    private static async Task CreateTestUserAsync(
        IAuth0Service apiAuth0Service,
        AquiferDbContext dbContext,
        HttpClient publisherClient,
        string testUserEmail,
        string testUserPassword,
        string testUserFirstName,
        string testUserLastName,
        UserRole testUserRole,
        CancellationToken ct)
    {
        // create user using API endpoint
        var createUserResponse = await publisherClient.POSTAsync<API.Endpoints.Users.Create.Endpoint, Request>(
            new Request
            {
                Email = testUserEmail,
                CompanyId = AquiferApiIntegrationTestsCompanyId,
                FirstName = testUserFirstName,
                LastName = testUserLastName,
                Role = testUserRole,
            });

        createUserResponse.EnsureSuccessStatusCode();

        // The created user will have a random password and got a password reset email (that we will ignore).
        // Therefore, we need to manually set the password.
        var createdUser = await dbContext.Users.Where(u => u.Email == testUserEmail).FirstOrDefaultAsync(ct) ??
            throw new InvalidOperationException($"Failed to create user with email \"{testUserEmail}\".");

        var managementApiAccessToken = await apiAuth0Service.GetAccessTokenAsync(ct);
        await apiAuth0Service.SetUserPasswordAsync(managementApiAccessToken, createdUser.ProviderId, testUserPassword, ct);
    }

    private void InitializeIntegrationTestAppHost()
    {
        Host = Microsoft.Extensions
            .Hosting
            .Host
            .CreateDefaultBuilder()
            .UseEnvironment(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Development)
            .ConfigureServices((context, services) =>
            {
                var isDevelopment = context.HostingEnvironment.EnvironmentName == Environments.Development;

                services.AddOptions<ConfigurationOptions>().Bind(context.Configuration);

                var configuration = context.Configuration.Get<ConfigurationOptions>() ??
                    throw new InvalidOperationException($"Unable to bind {nameof(ConfigurationOptions)}.");

                services.AddDbContext<AquiferDbContext>(options => options
                        .UseAzureSql(
                            configuration.ConnectionStrings.AquiferDb,
                            providerOptions => providerOptions.EnableRetryOnFailure(3))
                        .EnableSensitiveDataLogging(isDevelopment)
                        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking))
                    .AddAzureClient(true)
                    .AddSingleton<IAzureKeyVaultClient, AzureKeyVaultClient>()
                    .AddScoped<IAuth0Service, Auth0Service>()
                    .AddSingleton(cfg => cfg.GetService<IOptions<ConfigurationOptions>>()!.Value.IntegrationTestAuth0Settings)
                    .AddSingleton(cfg => cfg.GetService<IOptions<ConfigurationOptions>>()!.Value.IntegrationTestUserSettings);
            })
            .ConfigureLogging(loggingBuilder => loggingBuilder.AddConsole())
            .Build();

        // There's no need to start/stop the host; we're only using it to build configuration and services.
        //await Host.StartAsync();
    }

    public sealed class DevNullOutputCacheStore : IOutputCacheStore
    {
        private static readonly ValueTask<byte[]?> s_emptyGetValueTask = new();

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
        {
            return s_emptyGetValueTask;
        }

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}