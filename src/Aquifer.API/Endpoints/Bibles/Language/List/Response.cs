using Aquifer.Data.Schemas;

namespace Aquifer.API.Endpoints.Bibles.Language.List;

public record Response
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string Abbreviation { get; set; }
    public required int LanguageId { get; set; }
    public required bool RestrictedLicense { get; set; }
    public required BibleLicenseInfoSchema LicenseInfo { get; init; }
    public required IEnumerable<BibleBookMetadataResponse> Books { get; set; }
}

public record BibleBookMetadataResponse
{
    public required string BookCode { get; set; }
    public required string DisplayName { get; set; }
    public required int TextSize { get; set; }
    public required int AudioSize { get; set; }
    public required int ChapterCount { get; set; }
}