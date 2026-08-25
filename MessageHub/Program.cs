using MessageHub;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new CosmosOptions
    {
        ConnectionString = config["CosmosConnection"]
            ?? throw new InvalidOperationException("CosmosConnection is not configured."),
        DatabaseId = config["Cosmos:DatabaseId"] ?? "log",
        ContainerId = config["Cosmos:ContainerId"] ?? "notifications",
    };
});

// Singleton: CosmosClient is thread-safe and expensive to construct, and it owns the
// connection pool -- creating one per invocation is the classic way to exhaust sockets.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<CosmosOptions>();

    return new CosmosClient(options.ConnectionString, new CosmosClientOptions
    {
        // Gateway mode because the emulator does not serve the Direct-mode backend protocol.
        //
        // LimitToEndpoint stops the SDK doing region discovery and pins it to the configured
        // endpoint. Not strictly required here -- the emulator derives the endpoint it
        // advertises in writableLocations from the request's Host header, so it correctly
        // reports cosmosdb-emulator:8081 to this container and localhost:8081 to the host --
        // but a single-region emulator has nothing to discover, and pinning removes a failure
        // mode that would otherwise depend on that header being right.
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
        },
    });
});

builder.Services.AddSingleton<INotificationStore, CosmosNotificationStore>();
builder.Services.AddHostedService<CosmosBootstrapper>();

builder.Build().Run();
