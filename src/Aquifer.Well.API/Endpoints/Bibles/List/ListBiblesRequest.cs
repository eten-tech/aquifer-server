namespace Aquifer.Well.API.Endpoints.Bibles.List;

public class ListBiblesRequest
{
    /// <summary>
    /// Optional language to search on by internal Aquifer language id. If no language is specified, then Bibles for all languages will be
    /// returned.
    /// Only one of language id or code may be provided.
    /// </summary>
    public int? LanguageId { get; init; }

    /// <summary>
    /// If specified then only language default Bibles (for <c>true</c>) or language non-default Bibles (for <c>false</c>) will be returned. If no
    /// value is provided, then all Bibles will be returned.
    /// </summary>
    public bool? IsLanguageDefault { get; init; }
}