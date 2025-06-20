using System.ComponentModel;
using FastEndpoints;

namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.Passages.Get;

public sealed class GetPassagesForParentResourceRequest
{
    /// <summary>
    /// The language ID on which to filter. The parent resource must have resources with passages where the resource contents use this language.
    /// </summary>
    [QueryParam]
    [DefaultValue(0)]
    public int LanguageId { get; init; } = 0;

    /// <summary>
    /// The ID of the parent resource for which to find passages.
    /// </summary>
    public int ParentResourceId { get; init; }
}