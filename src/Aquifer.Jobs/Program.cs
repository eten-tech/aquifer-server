using System.Net;
using Aquifer.AI;
using Aquifer.Common.Clients;
using Aquifer.Common.Clients.Http.IpAddressLookup;
using Aquifer.Common.Messages;
using Aquifer.Common.Messages.Publishers;
using Aquifer.Common.Services;
using Aquifer.Data;
using Aquifer.Data.Services;
using Aquifer.Jobs.Configuration;
using Aquifer.Jobs.Services;
using Aquifer.Jobs.Services.TranslationProcessors;
using Aquifer.Jobs.Subscribers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, builder) => builder.SetBasePath(context.HostingEnvironment.ContentRootPath)
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", true, true))
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // This requires setting env var name AZURE_FUNCTIONS_ENVIRONMENT in Dev/QA and Prod
        var isDevelopment = context.HostingEnvironment.EnvironmentName == Environments.Development;

        var configuration = context.Configuration.Get<ConfigurationOptions>() ??
            throw new InvalidOperationException($"Unable to bind {nameof(ConfigurationOptions)}.");

        services.AddOptions<ConfigurationOptions>().Bind(context.Configuration);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.AzureStorageAccount);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.Cdn);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.Ffmpeg);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.OpenAi);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.OpenAiTranslation);
        services.AddSingleton(cfg => cfg.GetRequiredService<IOptions<ConfigurationOptions>>().Value.Upload);

        services.AddDbContext<AquiferDbContext>(options => options
            .UseAzureSql(
                configuration.ConnectionStrings.AquiferDb,
                providerOptions => providerOptions.EnableRetryOnFailure(3))
            .EnableSensitiveDataLogging(isDevelopment)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddAzureClient(isDevelopment);

        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddKeyedSingleton<IBlobStorageService, BlobStorageService>(
            UploadResourceContentAudioMessageSubscriber.AzureCdnStorageAccountServiceKey,
            (sp, _) => new BlobStorageService(
                configuration.AzureCdnStorageAccount,
                sp.GetRequiredService<IAzureClientService>(),
                sp.GetRequiredService<ILogger<BlobStorageService>>()));
        services.AddSingleton<IQueueClientFactory, QueueClientFactory>();
        services.AddTranslationProcessingServices();

        services.AddHttpClient<IIpAddressLookupHttpClient, IpAddressLookupHttpClient>()
            .AddPolicyHandler(
                HttpPolicyExtensions.HandleTransientHttpError()
                    .OrResult(res => res.StatusCode == HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(5), 2)));
        services.AddSingleton<IAzureKeyVaultClient, AzureKeyVaultClient>();
        services.AddScoped<IResourceHistoryService, ResourceHistoryService>();
        services.AddSingleton<IEmailMessagePublisher, EmailMessagePublisher>();
        services.AddSingleton<INotificationMessagePublisher, NotificationMessagePublisher>();
        services.AddSingleton<ITranslationService, OpenAiTranslationService>();
        services.AddSingleton<IEmailService, SendGridEmailService>();
    })
    .ConfigureLogging((hostingContext, logging) =>
    {
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            const string applicationInsightsProviderName =
                "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider";
            var defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName == applicationInsightsProviderName);
            if (defaultRule is not null)
            {
                options.Rules.Remove(defaultRule);
            }
        });

        logging.AddApplicationInsights(console => console.IncludeScopes = true);

        logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
    })
    .Build();

StaticLoggerFactory.LoggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

await host.RunAsync();