using Microsoft.Extensions.Logging;

namespace MessageHub.Extensions.Logger;

public static partial class StartupLoggerExtensions
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Cosmos ready: database '{database}', container '{container}', partition key '{partitionKeyPath}'.")]
    public static partial void LogCosmosReady(
        this ILogger logger, string database, string container, string partitionKeyPath);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Cosmos not ready (attempt {attempt}): {reason}. Retrying in {delaySeconds}s.")]
    public static partial void LogCosmosNotReady(
        this ILogger logger, int attempt, string reason, int delaySeconds);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error,
        Message = "Cosmos provisioning failed.")]
    public static partial void LogCosmosProvisioningFailed(this ILogger logger, Exception exception);
}
