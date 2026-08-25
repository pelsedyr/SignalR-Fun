using MessageHub.Exceptions;
using MessageHub.Extensions.Logger;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using static MessageHub.Static.Strings;

namespace MessageHub.Static.Seed;

/// <summary>
/// Creates the database and container at startup, because the emulator starts empty and a
/// `docker compose down -v` puts it back that way. Both calls are idempotent, so this costs
/// nothing once provisioned.
///
/// This is emulator-shaped, not production-shaped: in Azure the database and container would be
/// provisioned with Bicep/Terraform and the app would assume they already exist.
/// </summary>
public static class CosmosBootstrapper
{
    private const int MaxAttempts = 30;
    private const int RetryDelaySeconds = 2;

    public static async Task EnsureProvisionedAsync(
        CosmosClient client,
        string databaseId,
        string containerId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // The compose healthcheck should mean the emulator is ready, but the host-side
        // `func start` path has no such gate, so keep a bounded retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var database = await client.CreateDatabaseIfNotExistsAsync(
                    databaseId, cancellationToken: cancellationToken);

                await database.Database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(containerId, Cosmos.PartitionKeyPath),
                    throughput: Cosmos.DefaultThroughput,
                    cancellationToken: cancellationToken);

                logger.LogCosmosReady(databaseId, containerId, Cosmos.PartitionKeyPath);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogCosmosNotReady(attempt, ex.Message, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                throw new NotificationException(
                    string.Format(ExceptionMessages.Notifications.ProvisioningFailed, databaseId, containerId), ex);
            }
        }
    }
}
