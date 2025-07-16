using Aquifer.Data.Entities;

namespace Aquifer.Well.API.Endpoints.Resources.ParentResources.List;

public sealed class ListParentResourcesResponse
{
    public required int Id { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public required string ShortName { get; set; }
    public required ResourceType ResourceType { get; set; }
    public required ResourceLicenseInfo LicenseInfo { get; init; }

    public required IReadOnlyList<ParentResourcesLocalization> Localizations { get; init; }
}

public sealed class ParentResourcesLocalization
{
    public required int LanguageId { get; init; }
    public required string DisplayName { get; init; }
}