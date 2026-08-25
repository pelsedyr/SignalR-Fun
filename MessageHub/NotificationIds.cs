using Azure.Messaging.ServiceBus;

namespace MessageHub;

public static class NotificationIds
{
    // Cosmos rejects these in an id, and a caller controls MessageId, so it can't be trusted raw.
    private static readonly char[] Forbidden = ['/', '\\', '#', '?'];

    // The idempotency key. ServiceBusPostTool builds the message once and reuses it across
    // send retries, so MessageId is stable for a given logical notification -- which is what
    // lets a redelivery collide instead of duplicating.
    //
    // SequenceNumber is the fallback because it is assigned by the broker and unique per
    // queue, so a message with no usable MessageId still gets a deterministic id.
    public static string FromMessage(ServiceBusReceivedMessage message)
    {
        var id = message.MessageId;

        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Forbidden) >= 0 || id.Length > 1023)
        {
            return $"sb-{message.SequenceNumber}";
        }

        return id;
    }
}
