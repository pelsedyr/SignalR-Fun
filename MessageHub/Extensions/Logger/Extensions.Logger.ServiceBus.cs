using Microsoft.Extensions.Logging;

namespace MessageHub.Extensions.Logger;

public static partial class ServiceBusLoggerExtensions
{
    // Body is deliberately not logged: it is user content, and it ends up in telemetry once
    // Application Insights is enabled.
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Received message {messageId} ({contentType}).")]
    public static partial void LogMessageReceived(this ILogger logger, string messageId, string? contentType);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Stored notification {notificationId} for {receiverId}.")]
    public static partial void LogNotificationStored(this ILogger logger, string notificationId, string receiverId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Notification {notificationId} for {receiverId} was already stored; nudging anyway.")]
    public static partial void LogNotificationAlreadyStored(this ILogger logger, string notificationId, string receiverId);

    // Content is deliberately not logged, for the same reason the body isn't in
    // LogMessageReceived. This is the send-side twin of 1001.
    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Enqueued message {messageId} for {receiverId} on {queueName}.")]
    public static partial void LogMessageEnqueued(
        this ILogger logger, string messageId, string receiverId, string queueName);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error,
        Message = "Failed to enqueue message {messageId} for {receiverId} on {queueName}: {reason}.")]
    public static partial void LogEnqueueFailed(
        this ILogger logger, string messageId, string receiverId, string queueName, string reason,
        Exception exception);
}
