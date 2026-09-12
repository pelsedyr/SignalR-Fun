using Azure.Messaging.ServiceBus;

namespace MessageHub.Static;

/// <summary>
/// The rules that turn a Service Bus MessageId into a Cosmos document id, in one place
/// because both sides of the pipeline need them: the send side has to know whether an id it
/// is about to put on the queue will survive the trip, and the receive side has to derive
/// the id it writes.
/// </summary>
public static class NotificationIds
{
    /// <summary>Cosmos rejects these in an id, and a caller controls MessageId, so it can't be trusted raw.</summary>
    private static readonly char[] Forbidden = ['/', '\\', '#', '?'];

    /// <summary>Cosmos' id length limit.</summary>
    public const int MaxLength = 1023;

    public static bool IsUsable(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.IndexOfAny(Forbidden) < 0 && id.Length <= MaxLength;

    /// <summary>
    /// The idempotency key. Producers build the message once and reuse it across send
    /// retries, so MessageId is stable for a given logical notification — which is what lets
    /// a redelivery collide instead of duplicating.
    ///
    /// SequenceNumber is the fallback because it is assigned by the broker and unique per
    /// queue, so a message with no usable MessageId still gets a deterministic id.
    /// </summary>
    public static string FromMessage(ServiceBusReceivedMessage message) =>
        IsUsable(message.MessageId) ? message.MessageId : $"sb-{message.SequenceNumber}";
}
