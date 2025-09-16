using Aquifer.Data.Schemas;

namespace Aquifer.API.Endpoints.Bibles.List;

public record Response
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required string Abbreviation { get; set; }
    public required int LanguageId { get; set; }
    public required bool IsLanguageDefault { get; set; }
    public required bool RestrictedLicense { get; set; }
    public required bool GreekAlignment { get; set; }
    public required BibleLicenseInfoSchema LicenseInfo { get; init; }
}