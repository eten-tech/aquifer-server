using System.Net;
using Aquifer.Well.API.Services;
using FastEndpoints;

namespace Aquifer.Well.API.Endpoints.PushNotifications.Installations.Delete;

public class DeleteDeviceInstallationEndpoint(INotificationService notificationService) : Endpoint<DeleteDeviceInstallationRequest>
{
    public override void Configure()
    {
        Delete("/push-notifications/device-installation/{DeviceInstallationId}");
        Description(d => d.ProducesProblemFE());
        Summary( s =>
        {
            s.Summary = "Delete a device installation.";
            s.Description = "Delete a device installation with the relevant push notification service.";
        });
    }

    public override async Task HandleAsync(DeleteDeviceInstallationRequest req, CancellationToken ct)
    {
        var deletionSuccess = await notificationService.DeleteInstallationByIdAsync(
            req.DeviceInstallationId,
            ct);

        if (!deletionSuccess)
        {
            await SendAsync("Deleting the device installation Failed.", (int)HttpStatusCode.BadRequest, ct);
            return;
        }
        
        await SendNoContentAsync(ct);
    }
}
