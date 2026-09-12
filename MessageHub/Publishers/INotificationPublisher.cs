using MessageHub.Dto;

namespace MessageHub.Publishers;

public interface INotificationPublisher
{
    /// <summary>
    /// Puts a notification on the queue and returns the MessageId it assigned. That is the
    /// same value ServiceBusQueueTrigger will use as the Cosmos document id (see
    /// NotificationIds), so the caller can hand it back to the client as a correlation handle.
    ///
    /// Throws NotificationException when the send fails — callers turn that into a 503.
    /// </summary>
    Task<string> PublishAsync(NotificationDto notification, CancellationToken cancellationToken);
}
