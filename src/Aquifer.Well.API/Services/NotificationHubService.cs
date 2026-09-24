using Aquifer.Well.API.Endpoints.PushNotifications.Models;
using Microsoft.Extensions.Options;
using Microsoft.Azure.NotificationHubs;

namespace Aquifer.Well.API.Services;

public class NotificationHubService : INotificationService
{
    private readonly NotificationHubClient _hub;
    private readonly Dictionary<string, NotificationPlatform> _installationPlatform;
    private readonly ILogger<NotificationHubService> _logger;
    public NotificationHubService(IOptions<NotificationHubOptions> options, ILogger<NotificationHubService> logger)

    {
        _logger = logger;
        _hub = NotificationHubClient.CreateClientFromConnectionString(options.Value.ConnectionString, options.Value.Name);

        _installationPlatform = new Dictionary<string, NotificationPlatform>
        {
            { nameof(NotificationPlatform.Apns).ToLower(), NotificationPlatform.Apns },
            { nameof(NotificationPlatform.FcmV1).ToLower(), NotificationPlatform.FcmV1 }
        };
    }

    public async Task<bool> CreateOrUpdateInstallationAsync(DeviceInstallation deviceInstallation, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(deviceInstallation?.InstallationId) ||
            string.IsNullOrWhiteSpace(deviceInstallation?.Platform) ||
            string.IsNullOrWhiteSpace(deviceInstallation?.PushChannel))
        {
            return false;
        }

        var installation = new Installation()
        {
            InstallationId = deviceInstallation.InstallationId,
            PushChannel = deviceInstallation.PushChannel,
            Tags = deviceInstallation.Tags
        };

        if (_installationPlatform.TryGetValue(deviceInstallation.Platform, out var platform))
        {
            installation.Platform = platform;
        }
        else
        {
            return false;
        }

        try
        {
            await _hub.CreateOrUpdateInstallationAsync(installation, token);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public async Task<bool> DeleteInstallationByIdAsync(string installationId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return false;
        }

        try
        {
            await _hub.DeleteInstallationAsync(installationId, token);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public async Task<bool> RequestNotificationAsync(NotificationRequest notificationRequest, CancellationToken token)
    {
        if ((notificationRequest.Silent &&
            string.IsNullOrWhiteSpace(notificationRequest?.Action)) ||
            (!notificationRequest.Silent &&
             string.IsNullOrWhiteSpace(notificationRequest?.Text)) ||
            string.IsNullOrWhiteSpace(notificationRequest?.Action))
        {
            return false;
        }

        var androidPushTemplate = notificationRequest.Silent ?
            PushNotificationTemplates.Silent.Android :
            PushNotificationTemplates.Generic.Android;

        var iOSPushTemplate = notificationRequest.Silent ?
            PushNotificationTemplates.Silent.Ios :
            PushNotificationTemplates.Generic.Ios;

        var androidPayload = PrepareNotificationPayload(
            androidPushTemplate,
            notificationRequest.Text,
            notificationRequest.Action);

        var iOSPayload = PrepareNotificationPayload(
            iOSPushTemplate,
            notificationRequest.Text,
            notificationRequest.Action);

        try
        {
            if (notificationRequest.Tags.Length == 0)
            {
                // This will broadcast to all users registered in the notification hub
                await SendPlatformNotificationsAsync(androidPayload, iOSPayload, token);
            }
            else if (notificationRequest.Tags.Length <= 20)
            {
                await SendPlatformNotificationsAsync(androidPayload, iOSPayload, notificationRequest.Tags, token);
            }
            else
            {
                var notificationTasks = notificationRequest.Tags
                    .Select((value, index) => (value, index))
                    .GroupBy(g => g.index / 20, i => i.value)
                    .Select(tags => SendPlatformNotificationsAsync(androidPayload, iOSPayload, tags, token));

                await Task.WhenAll(notificationTasks);
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error sending notification");
            return false;
        }
    }

    private string PrepareNotificationPayload(string template, string text, string action)
    {
        return template
        .Replace("$(alertMessage)", text, StringComparison.InvariantCulture)
        .Replace("$(alertAction)", action, StringComparison.InvariantCulture);
    }

    private Task SendPlatformNotificationsAsync(string androidPayload, string _, CancellationToken token)
    {
        var sendTasks = new Task[]
        {
            _hub.SendFcmV1NativeNotificationAsync(androidPayload, token),
            // todo enable iOSPayload with iOS support
            //_hub.SendAppleNativeNotificationAsync(iOSPayload, token)
        };

        return Task.WhenAll(sendTasks);
    }

    private Task SendPlatformNotificationsAsync(string androidPayload, string _, IEnumerable<string> tags, CancellationToken token)
    {
        var sendTasks = new Task[]
        {
            _hub.SendFcmV1NativeNotificationAsync(androidPayload, tags, token),
            // todo enable iOSPayload with iOS support
            //_hub.SendAppleNativeNotificationAsync(iOSPayload, tags, token)
        };

        return Task.WhenAll(sendTasks);
    }
}