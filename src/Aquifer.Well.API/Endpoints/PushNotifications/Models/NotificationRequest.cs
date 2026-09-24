namespace Aquifer.Well.API.Endpoints.PushNotifications.Models;

public class NotificationRequest
{
    public required string Text { get; set; }
    public required string Action { get; set; }
    public string[] Tags { get; set; } = [];
    public bool Silent { get; set; }
}