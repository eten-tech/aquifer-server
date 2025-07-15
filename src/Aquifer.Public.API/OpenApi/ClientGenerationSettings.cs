using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;

namespace Aquifer.Public.API.OpenApi;

/// <summary>
/// Generate client code and make it available as a zip file per language at `/clients/language-id`.
/// See https://fast-endpoints.com/docs/swagger-support#api-client-generation.
/// We could also generate clients from the command line or on build if desired.
/// </summary>
public static class ClientGenerationSettings
{
    public const string ClientGenerationOutputCacheTag = "client-generation";

    private const string ClientsRouteName = "clients";

    public static IEndpointRouteBuilder ConfigureClientGeneration(
        this IEndpointRouteBuilder app,
        string swaggerDocumentName,
        TimeSpan clientZipCacheDuration)
    {
        app.MapApiClientEndpoint(
            $"/{ClientsRouteName}/cs",
            c =>
            {
                c.SwaggerDocumentName = swaggerDocumentName;
                c.Language = GenerationLanguage.CSharp;
                c.ClientNamespaceName = "Aquifer.API.Client";
                c.ClientClassName = "AquiferClient";
                c.OutputPath = Path.Combine(c.OutputPath, "cs", Path.GetRandomFileName());
                c.ExcludePatterns = [$"**/{ClientsRouteName}/**", "**/admin/**"];
            },
            o =>
            {
                o.CacheOutput(p => p.Expire(clientZipCacheDuration).Tag(ClientGenerationOutputCacheTag));
                o.WithTags("Clients");
                o.WithName("Download C# client.");
            });

        app.MapApiClientEndpoint(
            $"/{ClientsRouteName}/java",
            c =>
            {
                c.SwaggerDocumentName = swaggerDocumentName;
                c.Language = GenerationLanguage.Java;
                c.ClientNamespaceName = "org.aquifer.api.client";
                c.ClientClassName = "AquiferClient";
                c.OutputPath = Path.Combine(c.OutputPath, "java", Path.GetRandomFileName());
                c.ExcludePatterns = [$"**/{ClientsRouteName}/**"];
            },
            o =>
            {
                o.CacheOutput(p => p.Expire(clientZipCacheDuration).Tag(ClientGenerationOutputCacheTag));
                o.WithTags("Clients");
                o.WithName("Download Java client.");
            });

        app.MapApiClientEndpoint(
            $"/{ClientsRouteName}/py",
            c =>
            {
                c.SwaggerDocumentName = swaggerDocumentName;
                c.Language = GenerationLanguage.Python;
                c.ClientNamespaceName = "aquifer_api_client";
                c.ClientClassName = "AquiferClient";
                c.OutputPath = Path.Combine(c.OutputPath, "py", Path.GetRandomFileName());
                c.ExcludePatterns = [$"**/{ClientsRouteName}/**"];
            },
            o =>
            {
                o.CacheOutput(p => p.Expire(clientZipCacheDuration).Tag(ClientGenerationOutputCacheTag));
                o.WithTags("Clients");
                o.WithName("Download Python client.");
            });

        app.MapApiClientEndpoint(
            $"/{ClientsRouteName}/ts",
            c =>
            {
                c.SwaggerDocumentName = swaggerDocumentName;
                c.Language = GenerationLanguage.TypeScript;
                c.ClientNamespaceName = "aquifer-api-client";
                c.ClientClassName = "AquiferClient";
                c.OutputPath = Path.Combine(c.OutputPath, "ts", Path.GetRandomFileName());
                c.ExcludePatterns = [$"**/{ClientsRouteName}/**"];
            },
            o =>
            {
                o.CacheOutput(p => p.Expire(clientZipCacheDuration).Tag(ClientGenerationOutputCacheTag));
                o.WithTags("Clients");
                o.WithName("Download TypeScript client");
            });

        return app;
    }
}