using System.ComponentModel.DataAnnotations;
using Aquifer.Common.Configuration;

namespace Aquifer.API.Configuration;

public class ConfigurationOptions
{
    public required AzureStorageAccountOptions AzureStorageAccount { get; init; }
    public required ConnectionStringOptions ConnectionStrings { get; init; }
    public required JwtSettingOptions JwtSettings { get; init; }
    public required Auth0Settings Auth0Settings { get; init; }
    public required NotificationOptions NotificationOptions { get; init; }
    public required string KeyVaultUri { get; init; }
    public required UploadOptions Upload { get; init; }
}

public class NotificationOptions
{
    public string? NotifyUserIdsOnCommunityReviewerComment { get; init; }
}

public class ConnectionStringOptions
{
    public required string AquiferDb { get; init; }
}

public class JwtSettingOptions
{
    [Url]
    public required string Authority { get; init; }

    public required string Audience { get; init; }
}

public class Auth0Settings
{
    public required string ApiClientId { get; init; }
    public required string ApplicationClientId { get; set; }
    public required string Audience { get; init; }

    [Url]
    public required string BaseUri { get; init; }
}