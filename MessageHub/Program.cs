using Azure.Messaging.ServiceBus;
using MessageHub.Exceptions;
using MessageHub.Extensions.Logger;
using MessageHub.Publishers;
using MessageHub.Repositories;
using MessageHub.Static;
using MessageHub.Static.Seed;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static MessageHub.Static.Strings;

var builder = FunctionsApplication.CreateBuilder(args);

// The ASP.NET Core-integrated worker model. Required for NegotiateController to write its
// response body correctly — do not swap this for the plain worker defaults.
builder.ConfigureFunctionsWebApplication();

//Cosmos
// Singleton: CosmosClient is thread-safe and expensive to construct, and it owns the
// connection pool — creating one per invocation is the classic way to exhaust sockets.
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration[ConfigurationKeys.Cosmos.ConnectionString]
        ?? throw new ConfigurationException(
            string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                ConfigurationKeys.Cosmos.ConnectionString));

    return new CosmosClient(connectionString, new CosmosClientOptions
    {
        // Gateway mode because the emulator does not serve the Direct-mode backend protocol.
        //
        // LimitToEndpoint stops the SDK doing region discovery and pins it to the configured
        // endpoint. Not strictly required — the emulator derives the endpoint it advertises in
        // writableLocations from the request's Host header, so it correctly reports
        // cosmosdb-emulator:8081 to this container and localhost:8081 to the host — but a
        // single-region emulator has nothing to discover, and pinning removes a failure mode
        // that would otherwise depend on that header being right.
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
        },
    });
});

//Service Bus
// Singleton for the same reasons as CosmosClient: it owns the AMQP connection, and creating
// one per request would open and tear down a link on every send. ServiceBusClient is
// IAsyncDisposable, so the container disposes it at shutdown.
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration[ConfigurationKeys.ServiceBus.ConnectionString]
        ?? throw new ConfigurationException(
            string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                ConfigurationKeys.ServiceBus.ConnectionString));

    // Connection-string form only. With an identity-based connection Azure sets
    // ServiceBusConnection__fullyQualifiedNamespace instead and this lookup returns null;
    // that would need the (fqns, TokenCredential) overload. Every environment in this repo
    // — local.settings.json, both compose files — uses the connection string.
    return new ServiceBusClient(connectionString, new ServiceBusClientOptions
    {
        // host.json's extensions.serviceBus settings govern the *trigger*, not this client:
        // sending through the SDK opts out of them. That asymmetry matters here, because
        // docker-entrypoint.sh waits for the bus but never fails, so the host can and does
        // start with the bus still unavailable. The SDK default (60s TryTimeout x 3 retries)
        // would hang an HTTP request for minutes; bound it to something a browser can wait for.
        RetryOptions = new ServiceBusRetryOptions
        {
            MaxRetries = 2,
            TryTimeout = TimeSpan.FromSeconds(5),
        },
    });
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var queueName = configuration[ConfigurationKeys.ServiceBus.DefaultQueue]
        ?? throw new ConfigurationException(
            string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                ConfigurationKeys.ServiceBus.DefaultQueue));

    return serviceProvider.GetRequiredService<ServiceBusClient>().CreateSender(queueName);
});

//Publishers
builder.Services.AddSingleton<INotificationPublisher, NotificationPublisher>();

//Repositories
builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();

var host = builder.Build();

//Provisioning — the emulator starts empty, and `docker compose down -v` returns it to empty.
var configuration = host.Services.GetRequiredService<IConfiguration>();
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger(nameof(CosmosBootstrapper));

try
{
    await CosmosBootstrapper.EnsureProvisionedAsync(
        host.Services.GetRequiredService<CosmosClient>(),
        configuration[ConfigurationKeys.Cosmos.Database]
            ?? throw new ConfigurationException(
                string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                    ConfigurationKeys.Cosmos.Database)),
        configuration[ConfigurationKeys.Cosmos.Container]
            ?? throw new ConfigurationException(
                string.Format(ExceptionMessages.Configuration.MissingConfigurationKey,
                    ConfigurationKeys.Cosmos.Container)),
        startupLogger);
}
catch (Exception ex)
{
    startupLogger.LogCosmosProvisioningFailed(ex);
    throw;
}

await host.RunAsync();
