using System.Text.Json.Serialization;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;

namespace Aquifer.Public.API.Endpoints.Resources.Get;

public class Response
{
    public int Id { get; set; }
    public int ReferenceId { get; set; }
    public string Name { get; set; } = null!;
    public string LocalizedName { get; set; } = null!;

    /// <summary>
    /// DB Value (Tiptap string).
    /// </summary>
    [JsonIgnore]
    public string ContentValue { get; set; } = null!;

    public object Content { get; set; } = null!;

    public ResourceTypeMetadata Grouping { get; set; } = null!;
    public ResourceContentLanguage Language { get; set; } = null!;
}

public class ResourceTypeMetadata
{
    public ResourceType Type { get; init; }

    public string Name { get; set; } = null!;

    [JsonIgnore]
    public ResourceContentMediaType MediaTypeValue { get; set; }

    public string MediaType { get; set; } = null!;

    public required ParentResourceLicenseInfoSchema LicenseInfo { get; init; }
}

public class ResourceContentLanguage
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public ScriptDirection ScriptDirection { get; set; }
}