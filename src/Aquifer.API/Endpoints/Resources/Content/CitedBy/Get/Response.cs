using System.Text.Json.Serialization;
using Aquifer.Data.Entities;

namespace Aquifer.API.Endpoints.Resources.Content.CitedBy.Get;

public class Response
{
    public required IEnumerable<AssociatedContentResponse> CitedByContent { get; set; }
}

public class AssociatedContentResponse
{
    [JsonIgnore]
    public ResourceContentEntity? ResourceContent { get; set; }

    public int? ContentId => ResourceContent?.Id;
    public required string ParentResourceName { get; set; }
    public required string EnglishLabel { get; set; }
    public IEnumerable<ResourceContentMediaType> MediaTypes { get; set; } = null!;
}