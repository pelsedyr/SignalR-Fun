using Microsoft.Extensions.Logging;

namespace MessageHub.Extensions.Logger;

public static partial class NotificationsLoggerExtensions
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Returned {count} notification(s) for {userId} (unreadOnly={unreadOnly}).")]
    public static partial void LogNotificationsReturned(this ILogger logger, int count, string userId, bool unreadOnly);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "Marked notification {notificationId} read for {userId}.")]
    public static partial void LogNotificationMarkedRead(this ILogger logger, string notificationId, string userId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Notification {notificationId} was not found for {userId}.")]
    public static partial void LogNotificationNotFound(this ILogger logger, string notificationId, string userId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Duplicate write for notification {notificationId}; skipping.")]
    public static partial void LogDuplicateWriteSkipped(this ILogger logger, string notificationId);

    // Warning rather than Information: unlike the four above, this one records a request the
    // app refused. reason is always an ExceptionMessages.Http constant and never
    // caller-supplied text — that is what keeps user content out of telemetry.
    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning,
        Message = "Rejected send request: {reason}")]
    public static partial void LogSendRequestRejected(this ILogger logger, string reason);
}
