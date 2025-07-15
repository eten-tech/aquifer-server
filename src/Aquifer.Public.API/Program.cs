using System.Text.Json;
using System.Text.Json.Serialization;
using Aquifer.Common.Extensions;
using Aquifer.Common.Messages;
using Aquifer.Common.Middleware;
using Aquifer.Common.Services;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Public.API.Configuration;
using Aquifer.Public.API.OpenApi;
using Aquifer.Public.API.Services;
using Aquifer.Public.API.Telemetry;
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
    .AddDbContext<
        AquiferDbReadOnlyContext>(options =>
        options.UseAzureSql(
                configuration.ConnectionStrings.AquiferReadOnlyDb,
                providerOptions => providerOptions.EnableRetryOnFailure(3))
            .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking))
    .AddScoped<AquiferDbContext, AquiferDbReadOnlyContext>()
    .Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddSwaggerDocumentSettings()
    .AddFastEndpoints()
    .AddMemoryCache()
    .AddCachingServices()
    .AddSingleton<IQueueClientFactory, QueueClientFactory>()
    .AddTrackResourceContentRequestServices()
    .AddSingleton<ITelemetryInitializer, RequestTelemetryInitializer>()
    .AddAzureClient(builder.Environment.IsDevelopment())
    .AddOutputCache()
    .AddApplicationInsightsTelemetry()
    .AddHealthChecks()
    .AddDbContextCheck<AquiferDbReadOnlyContext>();

builder.Services.AddOptions<ConfigurationOptions>().Bind(builder.Configuration);
builder.Services.Configure<ApiKeyAuthorizationMiddlewareOptions>(o => o.Scope = ApiKeyScope.PublicApi);

var app = builder.Build();

StaticLoggerFactory.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

app.UseHealthChecks("/_health")
    .UseResponseCaching()
    .UseOutputCache()
    .UseFastEndpoints(config =>
    {
        config.Serializer.Options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        config.Endpoints.Configurator = ep => ep.AllowAnonymous();
    })
    .UseOpenApi()
    .UseReDoc(options =>
    {
        options.Path = "/docs";
        // hide the version number
        options.CustomInlineStyles = "h1 > span { display: none; }";
        options.DocumentTitle = "Aquifer API Documentation";
    })
    .UseMiddleware<ApiKeyAuthorizationMiddleware>()
    .UseResponseCachingVaryByAllQueryKeys();

app.ConfigureClientGeneration(SwaggerDocumentSettings.DocumentName, TimeSpan.FromDays(365));

app.Run();

// make this class public to access from integration tests
public partial class Program;