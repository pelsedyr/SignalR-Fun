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
}
