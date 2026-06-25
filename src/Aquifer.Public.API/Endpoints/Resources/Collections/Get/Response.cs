using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;

namespace Aquifer.Public.API.Endpoints.Resources.Collections.Get;

public sealed class Response
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required ResourceType ResourceType { get; init; }
    public required string? SliCategory { get; set; }
    public required int? SliLevel { get; set; }
    public required ParentResourceLicenseInfoSchema LicenseInfo { get; init; }
    public required IReadOnlyList<AvailableLanguageResponse> AvailableLanguages { get; init; }
}

public sealed class AvailableLanguageResponse
{
    public required int LanguageId { get; init; }
    public required string LanguageCode { get; init; }
    public required string DisplayName { get; init; }
    public required int ResourceItemCount { get; init; }
}