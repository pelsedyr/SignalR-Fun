using MessageHub.Dto;
using MessageHub.Models;

namespace MessageHub.Extensions;

public static class NotificationExtensions
{
    public static NotificationResponse ToResponse(this NotificationDocument document) =>
        new(document.Id, document.ReceiverId, document.Content, document.CreatedUtc, document.ReadUtc);

    public static NotificationNudge ToNudge(this NotificationDocument document) =>
        new(document.Id, document.CreatedUtc);
}
