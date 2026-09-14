namespace MessageHub.Static;

public static class Strings
{
    /// <summary>
    /// Configuration keys. Names are unchanged from before the restructure so
    /// docker-compose.yaml and local.settings.json keep working — this only stops them
    /// being magic strings scattered through the code.
    /// </summary>
    public static class ConfigurationKeys
    {
        public static class Cosmos
        {
            public const string ConnectionString = "CosmosConnection";
            public const string Database = "Cosmos:DatabaseId";
            public const string Container = "Cosmos:ContainerId";
        }

        public static class Notifications
        {
            public const string HubName = "Notifications:HubName";
        }

        public static class ServiceBus
        {
            // Intentionally the same literal as BindingExpressions.ServiceBusConnection: the
            // host resolves that one for [ServiceBusTrigger], IConfiguration resolves this one
            // for the ServiceBusClient in Program.cs. Two readers, one app setting — exactly
            // the pairing DefaultQueue below already has.
            public const string ConnectionString = "ServiceBusConnection";
            public const string DefaultQueue = "Email:ServiceBus:DefaultQueue";
        }
    }

    public static class QueryParameters
    {
        public const string UserId = "userId";
        public const string Unread = "unread";
    }

    /// <summary>
    /// Binding expressions. The %...% form is resolved by the Functions host against
    /// configuration, so these must match the ConfigurationKeys above.
    /// </summary>
    public static class BindingExpressions
    {
        public const string DefaultQueue = "%Email:ServiceBus:DefaultQueue%";
        public const string HubName = "%Notifications:HubName%";
        public const string SignalRConnection = "AzureSignalRConnectionString";
        public const string ServiceBusConnection = "ServiceBusConnection";
    }

    public static class Cosmos
    {
        public const string PartitionKeyPath = "/receiverId";

        /// <summary>Minimum manual RU/s. The emulator may ignore it; a real account will not.</summary>
        public const int DefaultThroughput = 400;
    }

    /// <summary>
    /// Bounds on what a caller may send. These are product limits, not Service Bus limits —
    /// the standard tier allows a 256 KB message, and a 256 KB notification is legal and
    /// terrible.
    /// </summary>
    public static class Notifications
    {
        public const int MaxContentLength = 4096;
        public const int MaxReceiverIdLength = 128;
    }

    public static class SignalR
    {
        /// <summary>The client-side event name. A nudge, never the notification itself.</summary>
        public const string NotificationReceived = "notificationReceived";
    }
}
