using Aquifer.Common.Clients;
using Aquifer.Common.Services;
using Aquifer.Common.Services.Caching;
using Aquifer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aquifer.Common.IntegrationTests;

/// <summary>
/// This class essentially acts as the Program.cs file for configuring the integration tests project's app config and services.
/// XUnit will automatically run the <see cref="InitializeAsync" /> and <see cref="DisposeAsync" /> methods when used in conjunction with
/// <see cref="TestBase{TAppFixture}" /> and will only do it only once per test class, not per test method, due to
/// the base class using <see cref="IClassFixture{TFixture}" />.
/// </summary>
public sealed class App : IAsyncLifetime
{
    private IHost Host { get; set; } = null!;

    private IServiceScope AppServiceScope { get; set; } = null!;

    public ValueTask InitializeAsync()
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

                services
                    .AddMemoryCache()
                    .AddDbContext<AquiferDbContext>(options => options
                        .UseAzureSql(
                            configuration.ConnectionStrings.AquiferDb,
                            providerOptions => providerOptions.EnableRetryOnFailure(3))
                        .EnableSensitiveDataLogging(isDevelopment)
                        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking))
                    .AddAzureClient(true)
                    .AddSingleton<IAzureKeyVaultClient, AzureKeyVaultClient>()
                    .AddScoped<ICachingVersificationService, CachingVersificationService>();
            })
            .ConfigureLogging(loggingBuilder => loggingBuilder.AddConsole())
            .Build();

        StaticLoggerFactory.LoggerFactory = Host.Services.GetRequiredService<ILoggerFactory>();

        // Tests really only need a Singleton scope, but then we can't share the same common service registration methods
        // for use by Web APIs that use Scoped registrations. This allows using them within a single Scope per test fixture.
        // If a scope per test method is needed then it will need to be done manually.
        var scopeFactory = Host.Services.GetRequiredService<IServiceScopeFactory>();
        AppServiceScope = scopeFactory.CreateScope();

        // There's no need to start/stop the host; we're only using it to build configuration and services.
        //await Host.StartAsync();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // There's no need to start/stop the host; we're only using it to build configuration and services.
        //await Host.StopAsync();

        AppServiceScope.Dispose();
        Host.Dispose();

        return ValueTask.CompletedTask;
    }

    public T GetRequiredService<T>() where T : notnull
    {
        return AppServiceScope.ServiceProvider.GetRequiredService<T>();
    }
}