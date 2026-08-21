namespace Aquifer.Well.API.Endpoints.PushNotifications.Models;

public class NotificationHubOptions
{
    public required string Name { get; set; }
    public required string ConnectionString { get; set; }
}