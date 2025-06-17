using Aquifer.Public.API.Helpers;
using FastEndpoints.Swagger;
using NSwag;

namespace Aquifer.Public.API.OpenApi;

public static class SwaggerDocumentSettings
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddSwaggerDocumentSettings(this IServiceCollection services)
    {
        return services.SwaggerDocument(sd =>
        {
            // turn off auto-grouping (but now must manually tag each endpoint using `WithTags()`)
            sd.AutoTagPathSegmentIndex = 0;

            sd.ShortSchemaNames = true;
            sd.EnableJWTBearerAuth = false;
            sd.EndpointFilter = ep => ep.EndpointTags?.Contains(EndpointHelpers.EndpointTags.ExcludeFromSwaggerDocument) != true;
            sd.DocumentSettings = ds =>
            {
                ds.DocumentName = DocumentName;
                ds.Title = "Aquifer API Documentation";
                ds.Description = """
                All endpoints require an API key in the `api-key` header.<br><br>

                The following videos are available for anyone new to working with APIs:

                <a href="https://cdn.aquifer.bible/training/aquifer-api-documentation.mp4" target="_blank">Understanding the documentation</a>

                <a href="https://cdn.aquifer.bible/training/aquifer-api-postman.mp4" target="_blank">Making requests with Postman</a>

                <a href="https://cdn.aquifer.bible/training/aquifer-api-files-demo-csharp.mp4" target="_blank">Downloading resources to the file system with C#</a>

                <a href="https://cdn.aquifer.bible/training/api-and-flat-files.mp4" target="_blank">Why use a web API instead of flat files?</a>

                <a href="https://cdn.aquifer.bible/training/webpage-search-with-react-demo.mp4" target="_blank">React Sample Application Demo</a>

                <br><br>
                Example applications using the Aquifer API can be found in the <a href="https://github.com/eten-tech/aquifer-api-samples" target="_blank">samples repository on GitHub</a>.
                """;
                ds.AddAuth(
                    "ApiKey",
                    new OpenApiSecurityScheme
                    {
                        Name = "api-key",
                        In = OpenApiSecurityApiKeyLocation.Header,
                        Type = OpenApiSecuritySchemeType.ApiKey,
                    });
                ds.MarkNonNullablePropsAsRequired();
                ds.SchemaSettings.SchemaProcessors.Add(new EnumSchemaProcessor());
                ds.OperationProcessors.Add(new DefaultParameterOperationProcessor());
            };
            sd.TagDescriptions = td =>
            {
                td["Resources"] = """
                These endpoints allow searching for resources and pulling down the associated content for each one.
                An individual resource is the individual content of a resource, such as a specific study note within
                a collection of associated study notes. A collection of resources is a group of individual resources belonging
                to the same collection. A resource type is the category to which a collection of resources can belong.

                As an example, for the Tyndale Bible Dictionary article for "Aaron", the resource is "Aaron", the
                title of the collection to which it belongs is "Bible Dictionary (Tyndale)", and the resource type
                is "Dictionary".
                """;
                td["Resources/Collections"] = "Endpoints for retrieving collections of resources.";
                td["Resources/Types"] = "Endpoints for retrieving the different types of resource collections and resources.";
                td["Languages"] = "Endpoints for pulling data specific to languages.";
                td["Bibles"] = "Endpoints for discovering available Bibles and pulling down Bible text and audio information.";
                td["Clients"] =
                    "Endpoints for downloading generated client source code for calling this API. Further details and usage instructions can be found [here](https://github.com/eten-tech/aquifer-api-samples/blob/master/documentation/client-generation.md)";
            };
        });
    }
}