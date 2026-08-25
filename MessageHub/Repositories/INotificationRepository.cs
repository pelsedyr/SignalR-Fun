using MessageHub.Models;

namespace MessageHub.Repositories;

public interface INotificationRepository
{
    /// <summary>
    /// Returns false when a notification with this id already exists, i.e. the message was
    /// already processed. Deliberately not an upsert — see NotificationRepository.
    /// </summary>
    Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationDocument>> GetForReceiverAsync(
        string receiverId,
        bool unreadOnly,
        CancellationToken cancellationToken);

    /// <summary>Returns false when the notification doesn't exist in that receiver's partition.</summary>
    Task<bool> MarkReadAsync(string receiverId, string id, CancellationToken cancellationToken);
}
