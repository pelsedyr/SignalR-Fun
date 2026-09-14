namespace MessageHub.Static;

public static partial class ExceptionMessages
{
    public static class Notifications
    {
        public const string DeserializationFailed = "Failed to deserialize Service Bus message. MessageId: {0}";
        public const string CreateFailed = "Failed to create notification with id: {0}";
        public const string RetrieveFailed = "Failed to retrieve notifications for receiver: {0}";
        public const string MarkReadFailed = "Failed to mark notification {0} as read for receiver: {1}";
        public const string ProvisioningFailed = "Failed to provision Cosmos database '{0}' and container '{1}'";
        public const string EnqueueFailed = "Failed to enqueue notification for receiver: {0}";
    }

    public static class Http
    {
        public const string MissingUserId = "userId is required.";
        public const string NotificationNotFound = "Notification '{0}' not found for this user.";
        public const string InvalidRequestBody = "Request body must be JSON with receiverId and content.";
        public const string MissingReceiverId = "receiverId is required.";
        public const string MissingContent = "content is required.";
        public const string ReceiverIdTooLong = "receiverId must be {0} characters or fewer.";
        public const string ContentTooLong = "content must be {0} characters or fewer.";
        public const string SendUnavailable = "Could not reach the notification queue. Try again.";
    }
}
