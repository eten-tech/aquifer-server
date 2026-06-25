using Aquifer.Data.Schemas;

namespace Aquifer.Well.API.Endpoints.Bibles.List;

public record ListBiblesResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Abbreviation { get; init; }
    public required int LanguageId { get; init; }
    public required bool IsLanguageDefault { get; init; }
    public required bool HasAudio { get; init; }
    public required bool HasGreekAlignment { get; init; }
    public required int ContentIteration { get; init; }
    public required BibleLicenseInfoSchema LicenseInfo { get; init; }
}