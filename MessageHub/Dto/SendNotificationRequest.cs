namespace MessageHub.Dto;

/// <summary>
/// Untrusted input, so both properties are nullable — unlike NotificationDto and
/// NotificationResponse, which are server-constructed. `required` or a non-nullable
/// positional record would be a lie: System.Text.Json will happily hand back an instance
/// with nulls for a body of `{}`. The nullability here is the reminder to validate.
/// </summary>
public record SendNotificationRequest(string? ReceiverId, string? Content);
