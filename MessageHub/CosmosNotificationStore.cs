using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace MessageHub;

public class CosmosNotificationStore : INotificationStore
{
    private readonly Container _container;
    private readonly ILogger<CosmosNotificationStore> _logger;

    public CosmosNotificationStore(CosmosClient client, CosmosOptions options, ILogger<CosmosNotificationStore> logger)
    {
        _container = client.GetContainer(options.DatabaseId, options.ContainerId);
        _logger = logger;
    }

    // CreateItem, never UpsertItem. Service Bus is at-least-once, so this runs again on
    // redelivery -- and an upsert would overwrite ReadUtc back to null, silently un-reading a
    // notification the user had already read. A 409 is the expected, benign outcome.
    public async Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await _container.CreateItemAsync(
                document,
                new PartitionKey(document.ReceiverId),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation(
                "Notification {id} already stored; skipping duplicate write.", document.Id);
            return false;
        }
    }

    public async Task<IReadOnlyList<NotificationDocument>> GetForUserAsync(
        string receiverId,
        bool unreadOnly,
        CancellationToken cancellationToken)
    {
        var sql = unreadOnly
            ? "SELECT * FROM c WHERE NOT IS_DEFINED(c.readUtc) OR IS_NULL(c.readUtc) ORDER BY c.createdUtc DESC"
            : "SELECT * FROM c ORDER BY c.createdUtc DESC";

        // Scoping by PartitionKey rather than a WHERE clause is what keeps this a
        // single-partition read -- and it makes it structurally impossible for one user's
        // query to return another user's rows, which matters because the spike has no auth.
        var iterator = _container.GetItemQueryIterator<NotificationDocument>(
            new QueryDefinition(sql),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(receiverId) });

        var results = new List<NotificationDocument>();
        while (iterator.HasMoreResults)
        {
            foreach (var document in await iterator.ReadNextAsync(cancellationToken))
            {
                results.Add(document);
            }
        }

        return results;
    }

    public async Task<bool> MarkReadAsync(string receiverId, string id, CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKey(receiverId);

        try
        {
            // Point read + replace, both keyed on (partition, id), so a caller cannot touch
            // another user's notification even by guessing an id.
            var existing = await _container.ReadItemAsync<NotificationDocument>(
                id, partitionKey, cancellationToken: cancellationToken);

            if (existing.Resource.ReadUtc is not null)
            {
                return true;
            }

            existing.Resource.ReadUtc = DateTimeOffset.UtcNow;
            await _container.ReplaceItemAsync(
                existing.Resource, id, partitionKey, cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
