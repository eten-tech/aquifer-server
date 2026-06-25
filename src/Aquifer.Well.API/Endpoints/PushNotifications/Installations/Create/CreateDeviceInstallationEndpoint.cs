using System.Net;
using Aquifer.Well.API.Endpoints.PushNotifications.Models;
using Aquifer.Well.API.Services;
using FastEndpoints;

namespace Aquifer.Well.API.Endpoints.PushNotifications.Installations.Create;

public class CreateDeviceInstallationEndpoint(INotificationService notificationService) : Endpoint<CreateDeviceInstallationRequest>
{
    public override void Configure()
    {
        Post("/push-notifications/device-installation");
        Description(d => d.ProducesProblemFE());
        Summary( s =>
        {
            s.Summary = "Create or update a device installation.";
            s.Description = "Create or update a device installation with the relevant push notification service.";
        });
    }

    public override async Task HandleAsync(CreateDeviceInstallationRequest req, CancellationToken ct)
    {
        var installationSuccess = await notificationService.CreateOrUpdateInstallationAsync(
            new DeviceInstallation {
                InstallationId = req.InstallationId,
                Platform = req.Platform,
                PushChannel = req.PushChannel,
                Tags = req.Tags ?? Array.Empty<string>()
            },
            ct);

        if (!installationSuccess)
        {
            await SendAsync("Installation Failed.", (int)HttpStatusCode.BadRequest, ct);
            return;
        }
        
        await SendNoContentAsync(ct);
    }
}
