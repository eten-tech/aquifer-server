using System.Net;
using Aquifer.Well.API.Endpoints.PushNotifications.Models;
using Aquifer.Well.API.Services;
using FastEndpoints;

namespace Aquifer.Well.API.Endpoints.PushNotifications.Requests;

public class NotificationRequestsEndpoint(INotificationService notificationService) : Endpoint<NotificationRequest>
{
    public override void Configure()
    {
        Post("/push-notifications/requests");
        Description(d => d.ProducesProblemFE());
        Summary( s =>
        {
            s.Summary = "Requests a push notification.";
            s.Description = "Requests a push notification be sent with the given content and action.";
        });
    }

    public override async Task HandleAsync(NotificationRequest req, CancellationToken ct)
    {
        if ((req.Silent && string.IsNullOrWhiteSpace(req?.Action)) ||
            (!req.Silent && !string.IsNullOrWhiteSpace(req?.Text)))
        {
            await SendAsync("Bad notification request.", (int)HttpStatusCode.BadRequest, ct);
        }
        
        var success = await notificationService.RequestNotificationAsync(req!, ct);
        Console.WriteLine("Requested");

        if (!success)
        {
            await SendAsync("Notification request failed.", (int)HttpStatusCode.BadRequest, ct);
            return;
        }
        
        await SendNoContentAsync(ct);
    }
}
