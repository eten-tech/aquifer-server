namespace Aquifer.API.Endpoints.Admin.ApiKeys.Create;

public sealed class Response
{
    public int Id { get; set; }
    public string ApiKey { get; set; } = null!;
}
