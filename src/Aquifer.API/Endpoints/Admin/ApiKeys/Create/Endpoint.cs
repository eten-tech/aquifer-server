using System.Security.Cryptography;
using Aquifer.API.Common;
using Aquifer.Data;
using Aquifer.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.API.Endpoints.Admin.ApiKeys.Create;

public class Endpoint(AquiferDbContext dbContext)
    : Endpoint<Request, Response>
{
    private const int ApiKeyLength = 64;

    public override void Configure()
    {
        Post("/admin/api-keys");
        Permissions(PermissionName.CreateApiKey);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var apiKey = new ApiKeyEntity
        {
            ApiKey = await GenerateUniqueApiKeyAsync(ct),
            Scope = request.Scope,
            Organization = request.Organization,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            UseCase = request.UseCase,
        };

        await dbContext.ApiKeys.AddAsync(apiKey, ct);
        await dbContext.SaveChangesAsync(ct);

        await SendOkAsync(new Response { Id = apiKey.Id, ApiKey = apiKey.ApiKey }, ct);
    }

    private async Task<string> GenerateUniqueApiKeyAsync(CancellationToken ct)
    {
        string candidateKey;
        do
        {
            candidateKey = RandomNumberGenerator.GetHexString(ApiKeyLength);
        } while (await dbContext.ApiKeys.AnyAsync(x => x.ApiKey == candidateKey, ct));

        return candidateKey;
    }
}
