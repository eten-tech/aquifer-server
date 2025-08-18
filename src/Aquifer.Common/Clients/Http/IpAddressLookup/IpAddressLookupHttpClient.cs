using Aquifer.Common.Utilities;
using Microsoft.Extensions.Logging;

namespace Aquifer.Common.Clients.Http.IpAddressLookup;

public interface IIpAddressLookupHttpClient
{
    Task<IpAddressLookupResponse> LookupIpAddressAsync(string ipAddress, CancellationToken ct);
}

public class IpAddressLookupHttpClient : IIpAddressLookupHttpClient
{
    private const string BaseUri = "https://ipapi.co";
    private readonly HttpClient _httpClient;
    private readonly ILogger<IpAddressLookupHttpClient> _logger;

    public IpAddressLookupHttpClient(HttpClient httpClient, ILogger<IpAddressLookupHttpClient> logger)
    {
        _logger = logger;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BaseUri);
        // request will fail without User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "bn-user");
    }

    public async Task<IpAddressLookupResponse> LookupIpAddressAsync(string ipAddress, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{ipAddress}/json", ct);
        if (response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            return JsonUtilities.DefaultDeserialize<IpAddressLookupResponse>(responseContent);
        }

        _logger.LogError(
            "Unable to fetch IP address information for {ipAddress}. Response code: {responseCode}; Response: {response}.",
            ipAddress,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(ct));

        throw new Exception($"IP address lookup failed for {ipAddress}.");
    }
}