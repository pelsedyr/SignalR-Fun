using Azure.Messaging.ServiceBus;
using MessageHub.Static;

namespace MessageHub.Extensions;

public static class ServiceBusReceivedMessageExtensions
{
    /// <summary>
    /// The idempotency key. See NotificationIds for the rules and why they live there rather
    /// than here — the send side applies the same ones before it puts an id on the queue.
    /// </summary>
    public static string ToNotificationId(this ServiceBusReceivedMessage message) =>
        NotificationIds.FromMessage(message);
}
