namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.Passages.Get;

public sealed class GetPassagesForParentResourceResponse
{
    public required int StartVerseId { get; init; }
    public required int EndVerseId { get; init; }
}