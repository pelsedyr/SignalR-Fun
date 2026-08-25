namespace MessageHub;

public interface INotificationStore
{
    // Returns false when a document with this id already exists, i.e. the message was already
    // processed. Deliberately not an upsert -- see CosmosNotificationStore.
    Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDocument>> GetForUserAsync(
        string receiverId,
        bool unreadOnly,
        CancellationToken cancellationToken);

    // Returns false when the notification doesn't exist in that user's partition.
    Task<bool> MarkReadAsync(string receiverId, string id, CancellationToken cancellationToken);
}
