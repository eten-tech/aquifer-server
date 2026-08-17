using Aquifer.Data.Entities;

namespace Aquifer.API.Endpoints.Admin.ApiKeys.Create;

public sealed class Request
{
    public ApiKeyScope Scope { get; set; }
    public string? Organization { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? UseCase { get; set; }
}
