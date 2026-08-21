using System.Text.Json;
using System.Text.Json.Serialization;
using Aquifer.Common.Extensions;
using Aquifer.Common.Messages;
using Aquifer.Common.Middleware;
using Aquifer.Common.Services;
using Aquifer.Common.Services.Caching;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Well.API.Configuration;
using Aquifer.Well.API.OpenApi;
using Aquifer.Well.API.Endpoints.PushNotifications.Models;
using Aquifer.Well.API.Services;
using Aquifer.Well.API.Telemetry;
using FastEndpoints;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder.Configuration.Get<ConfigurationOptions>() ??
    throw new InvalidOperationException($"Unable to bind {nameof(ConfigurationOptions)}.");

builder.Services.AddOptions<ConfigurationOptions>().Bind(builder.Configuration);

builder.Services
    .AddSingleton(cfg => cfg.GetService<IOptions<ConfigurationOptions>>()!.Value.AzureStorageAccount)
    .AddDbContext<AquiferDbReadOnlyContext>(options => options
        .UseAzureSql(
            configuration.ConnectionStrings.AquiferReadOnlyDb,
            providerOptions => providerOptions.EnableRetryOnFailure(3))
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking))
    .AddScoped<AquiferDbContext, AquiferDbReadOnlyContext>()
    .Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddSwaggerDocumentSettings()
    .AddFastEndpoints()
    .AddMemoryCache()
    .AddScoped<ICachingLanguageService, CachingLanguageService>()
    .AddScoped<ICachingApiKeyService, CachingApiKeyService>()
    .AddSingleton<IQueueClientFactory, QueueClientFactory>()
    .AddTrackResourceContentRequestServices()
    .AddSingleton<ITelemetryInitializer, RequestTelemetryInitializer>()
    .AddAzureClient(builder.Environment.IsDevelopment())
    .AddOutputCache()
    .AddApplicationInsightsTelemetry()
    .AddHealthChecks()
    .AddDbContextCheck<AquiferDbReadOnlyContext>();

builder.Services.AddOptions<ConfigurationOptions>().Bind(builder.Configuration);
builder.Services.Configure<ApiKeyAuthorizationMiddlewareOptions>(o => o.Scope = ApiKeyScope.WellApi);

builder.Services.AddSingleton<INotificationService, NotificationHubService>();
builder.Services.AddOptions<NotificationHubOptions>()
    .Configure(builder.Configuration.GetSection("NotificationHub").Bind)
    .ValidateDataAnnotations();

var app = builder.Build();

StaticLoggerFactory.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

app.UseHealthChecks("/_health")
    .UseMiddleware<ApiKeyAuthorizationMiddleware>()
    .UseResponseCaching()
    .UseOutputCache()
    .UseFastEndpoints(config =>
    {
        config.Serializer.Options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        config.Endpoints.Configurator = ep => ep.AllowAnonymous();
        config.Versioning.Prefix = "v";
    })
    .UseOpenApi()
    .UseResponseCachingVaryByAllQueryKeys();

await app.GenerateApiClientAsync(SwaggerDocumentSettings.DocumentName);

app.Run();

// make this class public to access from integration tests
public partial class Program;