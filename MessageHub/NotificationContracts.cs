using System.Text.Json.Serialization;

namespace MessageHub;

// What GET /api/notifications returns. Built only via ToResponse below, so there is exactly
// one shape the client can render -- the point of the nudge design is that the SignalR push
// never carries a second, parallel shape that could drift from this one.
public record NotificationResponse(
    string Id,
    string ReceiverId,
    string Content,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ReadUtc);

// What goes over SignalR. Deliberately carries no Content: including it would be enough to
// render without fetching, which reintroduces the second shape this design avoids.
//
// Explicit names because the SignalR output binding serializes with .NET defaults (PascalCase),
// which would otherwise disagree with the camelCase the REST API returns.
public record NotificationNudge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc);

public static class NotificationProjections
{
    public static NotificationResponse ToResponse(this NotificationDocument document) =>
        new(document.Id, document.ReceiverId, document.Content, document.CreatedUtc, document.ReadUtc);

    public static NotificationNudge ToNudge(this NotificationDocument document) =>
        new(document.Id, document.CreatedUtc);
}
