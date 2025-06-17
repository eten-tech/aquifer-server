using Aquifer.AI;
using Aquifer.Common.Configuration;
using Aquifer.Jobs.Subscribers;

namespace Aquifer.Jobs.Configuration;

public class ConfigurationOptions
{
    public required string AquiferAdminBaseUri { get; init; }
    public required string AquiferApiBaseUri { get; init; }
    public required AzureStorageAccountOptions AzureCdnStorageAccount { get; init; }
    public required AzureStorageAccountOptions AzureStorageAccount { get; init; }
    public required CdnOptions Cdn { get; init; }
    public required FfmpegOptions Ffmpeg { get; init; }
    public required string KeyVaultUri { get; init; }
    public required ConnectionStringOptions ConnectionStrings { get; init; }
    public required EmailOptions Email { get; init; }
    public required MarketingEmailOptions MarketingEmail { get; init; }
    public required NotificationsOptions Notifications { get; init; }
    public required OpenAiOptions OpenAi { get; init; }
    public required OpenAiTranslationOptions OpenAiTranslation { get; init; }
    public required UploadOptions Upload { get; init; }
}

public class ConnectionStringOptions
{
    public required string AquiferDb { get; init; }
}

public class EmailOptions
{
    public required string SubjectPrefix { get; init; }
}

public class MarketingEmailOptions
{
    public required string Address { get; init; }
    public required string Name { get; init; }
    public required string ResourceLink { get; init; }
}

public class NotificationsOptions
{
    public required string SendResourceAssignmentNotificationTemplateId { get; init; }
    public required string SendProjectStartedNotificationTemplateId { get; init; }
}