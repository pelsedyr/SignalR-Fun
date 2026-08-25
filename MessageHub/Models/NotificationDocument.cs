namespace MessageHub.Models;

// The durable record. Cosmos is the inbox; SignalR only ever nudges the client to re-read
// from here. See the notifications-* notes for why this split exists.
//
// Property names are serialized camelCase via CosmosSerializationOptions in Program.cs, so
// no per-property attributes are needed.
public class NotificationDocument
{
    // Derived from the Service Bus MessageId, which makes the write idempotent under
    // at-least-once delivery: a redelivered message collides on this id instead of
    // producing a second document. See NotificationIds.FromMessage.
    public required string Id { get; init; }

    // Partition key (/receiverId). Every read in this app is "notifications for one user",
    // so scoping by it keeps those reads single-partition.
    public required string ReceiverId { get; init; }

    public required string Content { get; init; }

    // From ServiceBusReceivedMessage.EnqueuedTime rather than the DTO's DateOnly Date --
    // authoritative UTC with millisecond precision, and it needs no change to the wire
    // contract shared with cli/ServiceBusPostTool.
    public required DateTimeOffset CreatedUtc { get; init; }

    // null = unread. Mutable: this is the one field mark-as-read updates in place, and the
    // reason the trigger must not upsert (an upsert on redelivery would reset it to null).
    public DateTimeOffset? ReadUtc { get; set; }
}
