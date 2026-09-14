namespace MessageHub.Dto;

/// <summary>
/// The 202 body. Id is the Service Bus MessageId, which is also the id the queue trigger will
/// use in Cosmos (see NotificationIds) — so the caller can correlate this response with what
/// later shows up in GET /api/notifications.
///
/// Deliberately carries no timestamp: the only timestamp that matters is CreatedUtc, and that
/// comes from the broker's EnqueuedTime, which this side does not know yet.
/// </summary>
public record SendNotificationResponse(string Id, string ReceiverId);
