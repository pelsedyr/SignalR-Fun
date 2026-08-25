namespace MessageHub.Dto;

/// <summary>
/// What GET /api/notifications returns. Built only via NotificationExtensions.ToResponse,
/// so there is exactly one shape the client can render — the point of the nudge design is
/// that the SignalR push never carries a second, parallel shape that could drift from this.
/// </summary>
public record NotificationResponse(
    string Id,
    string ReceiverId,
    string Content,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ReadUtc);
