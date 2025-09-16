using Aquifer.Common.Extensions;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;
using Aquifer.Public.API.Helpers;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Public.API.Endpoints.Resources.Types.List;

public class Endpoint(AquiferDbReadOnlyContext dbContext) : EndpointWithoutRequest<IReadOnlyList<Response>>
{
    public override void Configure()
    {
        Get("/resources/types");
        Options(EndpointHelpers.UnauthenticatedServerCacheInSeconds(EndpointHelpers.TenMinutesInSeconds));
        Description(d => d.WithTags("Resources/Types"));
        Summary(s =>
        {
            s.Summary = "Get a list of available resource types and collections.";
            s.Description = """
            Get a list of resource types that are available. Within each resource type will be a list of resource collections
            that belong to that resource type. For example, Dictionary is a resource type, and "Bible Dictionary (Tyndale)"
            is a collection of resources within that type. The resource type and collection code can be used in other queries.
            """;
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var resourceTypes = Enum.GetValues(typeof(ResourceType)).Cast<ResourceType>().Skip(1).ToList();
        var parentResources = await dbContext.ParentResources.Where(x => x.Enabled).ToListAsync(ct);

        List<Response> response = [];
        foreach (var type in resourceTypes)
        {
            response.Add(
                new Response
                {
                    Type = type.GetDisplayName(),
                    Collections = parentResources.Where(x => x.ResourceType == type)
                        .Select(x => new AvailableResourceCollection
                        {
                            Code = x.Code,
                            Title = x.DisplayName,
                            LicenseInformation = JsonUtilities.DefaultDeserialize<ParentResourceLicenseInfoSchema>(x.LicenseInfo),
                        })
                        .ToList(),
                });
        }

        await SendAsync([.. response.OrderBy(x => x.Type)], 200, ct);
    }
}