using System.Net;
using MessageHub.Exceptions;
using MessageHub.Extensions.Logger;
using MessageHub.Models;
using MessageHub.Static;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static MessageHub.Static.Strings;

namespace MessageHub.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly Container _notificationCollection;
    private readonly ILogger<NotificationRepository> _logger;

    public NotificationRepository(
        IConfiguration configuration, CosmosClient client, ILogger<NotificationRepository> logger)
    {
        var databaseId = configuration[ConfigurationKeys.Cosmos.Database]
            ?? throw new ConfigurationException(
                string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                    ConfigurationKeys.Cosmos.Database));

        var containerId = configuration[ConfigurationKeys.Cosmos.Container]
            ?? throw new ConfigurationException(
                string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                    ConfigurationKeys.Cosmos.Container));

        _notificationCollection = client.GetDatabase(databaseId).GetContainer(containerId);
        _logger = logger;
    }

    /// <summary>
    /// CreateItem, never UpsertItem. Service Bus is at-least-once, so this runs again on
    /// redelivery — and an upsert would overwrite ReadUtc back to null, silently un-reading a
    /// notification the user had already read. A 409 is the expected, benign outcome.
    /// </summary>
    public async Task<bool> TryCreateAsync(NotificationDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await _notificationCollection.CreateItemAsync(
                document,
                new PartitionKey(document.ReceiverId),
                cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogDuplicateWriteSkipped(document.Id);
            return false;
        }
        catch (Exception ex)
        {
            throw new NotificationException(
                string.Format(ExceptionMessages.Notifications.CreateFailed, document.Id), ex);
        }
    }

    public async Task<IReadOnlyList<NotificationDocument>> GetForReceiverAsync(
        string receiverId,
        bool unreadOnly,
        CancellationToken cancellationToken)
    {
        var sql = unreadOnly
            ? "SELECT * FROM c WHERE NOT IS_DEFINED(c.readUtc) OR IS_NULL(c.readUtc) ORDER BY c.createdUtc DESC"
            : "SELECT * FROM c ORDER BY c.createdUtc DESC";

        try
        {
            // Scoping by PartitionKey rather than a WHERE clause is what keeps this a
            // single-partition read — and it makes it structurally impossible for one user's
            // query to return another user's rows, which matters because the spike has no auth.
            var iterator = _notificationCollection.GetItemQueryIterator<NotificationDocument>(
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
        catch (Exception ex)
        {
            throw new NotificationException(
                string.Format(ExceptionMessages.Notifications.RetrieveFailed, receiverId), ex);
        }
    }

    public async Task<bool> MarkReadAsync(string receiverId, string id, CancellationToken cancellationToken)
    {
        var partitionKey = new PartitionKey(receiverId);

        try
        {
            // Point read + replace, both keyed on (partition, id), so a caller cannot touch
            // another user's notification even by guessing an id.
            var existing = await _notificationCollection.ReadItemAsync<NotificationDocument>(
                id, partitionKey, cancellationToken: cancellationToken);

            if (existing.Resource.ReadUtc is not null)
            {
                return true;
            }

            existing.Resource.ReadUtc = DateTimeOffset.UtcNow;
            await _notificationCollection.ReplaceItemAsync(
                existing.Resource, id, partitionKey, cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            throw new NotificationException(
                string.Format(ExceptionMessages.Notifications.MarkReadFailed, id, receiverId), ex);
        }
    }
}
