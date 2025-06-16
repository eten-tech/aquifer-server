namespace Aquifer.Common.IntegrationTests;

public sealed class ConfigurationOptions
{
    public required ConnectionStringOptions ConnectionStrings { get; init; }
    public required string KeyVaultUri { get; init; }
}

public sealed class ConnectionStringOptions
{
    public required string AquiferDb { get; init; }
    public required string AzureStorageAccount { get; init; }
}