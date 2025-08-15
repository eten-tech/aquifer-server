using Aquifer.Common.Utilities;
using Microsoft.Extensions.Logging;
using System.Net;

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
        const int maxAttempts = 5;
        var baseDelay = TimeSpan.FromMilliseconds(500);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await _httpClient.GetAsync($"{ipAddress}/json", ct);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                return JsonUtilities.DefaultDeserialize<IpAddressLookupResponse>(responseContent);
            }
            
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                && attempt < maxAttempts)
            {
                TimeSpan? retryDelay = null;

                // The docs at https://ipapi.co/api/#errors say nothing of requests containing retry value headers.
                // So, we will default to calculating our own reasonable retry delay.
                retryDelay ??= CalculateRetryDelay(baseDelay, attempt);

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rate limited by ipapi.co for {ipAddress}. Attempt {attempt}/{maxAttempts}. Waiting {delay} before retry. Response: {response}",
                    ipAddress,
                    attempt,
                    maxAttempts,
                    retryDelay,
                    errorBody);

                await Task.Delay(retryDelay.Value, ct);
                continue;
            }
            
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Unable to fetch IP address information for {ipAddress}. Response code: {responseCode}; Response: {response}.",
                ipAddress,
                response.StatusCode,
                responseBody);
        }

        throw new Exception($"IP address lookup failed for {ipAddress} after retries due to rate limiting.");
    }

    private static TimeSpan CalculateRetryDelay(TimeSpan baseDelay, int attempt)
    {
        var backoff = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var jitterMs = Random.Shared.Next(0, 250);
        var retryDelay = backoff + TimeSpan.FromMilliseconds(jitterMs);
        
        if (retryDelay > TimeSpan.FromSeconds(10))
        {
            retryDelay = TimeSpan.FromSeconds(10);
        }
        
        return retryDelay;
    } 
}