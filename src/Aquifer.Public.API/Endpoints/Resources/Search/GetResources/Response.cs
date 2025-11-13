using System.Text.Json.Serialization;
using Aquifer.Data.Entities;

namespace Aquifer.Public.API.Endpoints.Resources.Search.GetResources;

public class Response
{
    public int TotalItemCount { get; set; }
    public int ReturnedItemCount { get; set; }
    public int Offset { get; set; }
    public IReadOnlyList<ResponseContent> Items { get; set; } = [];
}

public class ResponseContent
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string LocalizedName { get; set; } = null!;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResourceContentVersionReviewLevel ReviewLevel { get; set; }

    public ResourceContentMediaType MediaType { get; set; }

    public string LanguageCode { get; set; } = null!;
    public ResourceTypeMetadata Grouping { get; set; } = null!;
}

public class ResourceTypeMetadata
{
    public ResourceType Type { get; init; }

    public string Name { get; set; } = null!;
    public string CollectionTitle { get; set; } = null!;
    public string CollectionCode { get; set; } = null!;
}