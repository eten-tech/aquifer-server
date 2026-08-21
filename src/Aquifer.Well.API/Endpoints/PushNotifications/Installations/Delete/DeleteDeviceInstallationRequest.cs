namespace Aquifer.Well.API.Endpoints.PushNotifications.Installations.Delete;

public sealed class DeleteDeviceInstallationRequest
{
    public required string DeviceInstallationId { get; set; }
}