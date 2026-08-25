using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessageHub;

// Creates the database and container at startup, because the emulator starts empty and a
// `docker compose down -v` puts it back that way. Both calls are idempotent, so this costs
// nothing once provisioned.
//
// This is emulator-shaped, not production-shaped: in Azure the database and container would be
// provisioned with Bicep/Terraform and the app would assume they already exist.
public class CosmosBootstrapper : IHostedService
{
    private readonly CosmosClient _client;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosBootstrapper> _logger;

    public CosmosBootstrapper(CosmosClient client, CosmosOptions options, ILogger<CosmosBootstrapper> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The compose healthcheck should mean the emulator is ready, but the host-side
        // `func start` path has no such gate, so keep a bounded retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var database = await _client.CreateDatabaseIfNotExistsAsync(
                    _options.DatabaseId, cancellationToken: cancellationToken);

                await database.Database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(_options.ContainerId, CosmosOptions.PartitionKeyPath),
                    // Minimum manual RU/s. The emulator may ignore it; specifying it keeps the
                    // same call valid against a real account.
                    throughput: 400,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Cosmos ready: database '{database}', container '{container}', partition key '{pk}'.",
                    _options.DatabaseId, _options.ContainerId, CosmosOptions.PartitionKeyPath);
                return;
            }
            catch (Exception ex) when (attempt < 30 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Cosmos not ready (attempt {attempt}): {message}. Retrying in 2s.", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
