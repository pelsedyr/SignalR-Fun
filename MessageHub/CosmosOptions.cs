namespace MessageHub;

public class CosmosOptions
{
    public required string ConnectionString { get; init; }
    public required string DatabaseId { get; init; }
    public required string ContainerId { get; init; }

    // Partition key path. Every read is "notifications for one user", so partitioning on the
    // recipient keeps those reads single-partition and gives high cardinality (one logical
    // partition per user) with no hot partition.
    public const string PartitionKeyPath = "/receiverId";
}
