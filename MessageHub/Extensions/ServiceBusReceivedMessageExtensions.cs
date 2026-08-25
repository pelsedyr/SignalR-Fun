using Azure.Messaging.ServiceBus;

namespace MessageHub.Extensions;

public static class ServiceBusReceivedMessageExtensions
{
    /// <summary>Cosmos rejects these in an id, and a caller controls MessageId, so it can't be trusted raw.</summary>
    private static readonly char[] Forbidden = ['/', '\\', '#', '?'];

    /// <summary>
    /// The idempotency key. ServiceBusPostTool builds the message once and reuses it across
    /// send retries, so MessageId is stable for a given logical notification — which is what
    /// lets a redelivery collide instead of duplicating.
    ///
    /// SequenceNumber is the fallback because it is assigned by the broker and unique per
    /// queue, so a message with no usable MessageId still gets a deterministic id.
    /// </summary>
    public static string ToNotificationId(this ServiceBusReceivedMessage message)
    {
        var id = message.MessageId;

        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Forbidden) >= 0 || id.Length > 1023)
        {
            return $"sb-{message.SequenceNumber}";
        }

        return id;
    }
}
