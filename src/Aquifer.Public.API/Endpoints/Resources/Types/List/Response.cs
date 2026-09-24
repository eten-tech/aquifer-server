using Aquifer.Data.Schemas;

namespace Aquifer.Public.API.Endpoints.Resources.Types.List;

public class Response
{
    public required string Type { get; set; }
    public List<AvailableResourceCollection> Collections { get; set; } = [];
}

public class AvailableResourceCollection
{
    public required string Code { get; set; }
    public required string Title { get; set; }
    public required ParentResourceLicenseInfoSchema LicenseInformation { get; init; }
}