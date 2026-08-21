using System.Text.Json;
using Azure.Messaging.ServiceBus;

var connectionString = GetConfiguration(
    "SERVICEBUS_CONNECTION",
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

var queueName = GetConfiguration("SERVICEBUS_QUEUE", "d-avdekl-notifications");
var startupTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_STARTUP_TIMEOUT_SECONDS"), out var parsed)
    ? Math.Max(5, parsed)
    : 120;
var retryDelaySeconds = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_RETRY_SECONDS"), out parsed)
    ? Math.Max(1, parsed)
    : 3;

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

await using var client = new ServiceBusClient(connectionString);
await using var sender = client.CreateSender(queueName);

// One-shot mode: dotnet run -- <receiverId> <content...>
if (args.Length > 0)
{
    var receiverId = args[0];
    var content = string.Join(' ', args.Skip(1));
    if (string.IsNullOrWhiteSpace(content))
    {
        Console.Error.WriteLine("Usage: dotnet run -- <receiverId> <content>");
        Environment.ExitCode = 1;
        return;
    }

    await SendNotificationAsync(new NotificationDto(receiverId, content), cancellationSource.Token);
    return;
}

// Interactive mode
Console.WriteLine($"Posting to queue '{queueName}'.");
Console.WriteLine("Enter a Receiver ID and Content for each message. Leave Receiver ID blank, or press Ctrl+C, to exit.");
Console.WriteLine();

while (!cancellationSource.IsCancellationRequested)
{
    Console.Write("Receiver ID: ");
    var receiverId = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(receiverId) || cancellationSource.IsCancellationRequested)
        break;

    Console.Write("Content:     ");
    var content = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(content) || cancellationSource.IsCancellationRequested)
        break;

    await SendNotificationAsync(new NotificationDto(receiverId, content), cancellationSource.Token);
    Console.WriteLine();
}

Console.WriteLine("Done.");

async Task SendNotificationAsync(NotificationDto notification, CancellationToken cancellationToken)
{
    var message = new ServiceBusMessage(JsonSerializer.Serialize(notification))
    {
        MessageId = Guid.NewGuid().ToString(),
        ContentType = "application/json",
        Subject = nameof(NotificationDto)
    };

    var startupDeadline = DateTime.UtcNow.AddSeconds(startupTimeoutSeconds);
    while (true)
    {
        try
        {
            await sender.SendMessageAsync(message, cancellationToken);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss} SENT] MessageId: {message.MessageId} -> {queueName}");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ServiceBusException ex) when (IsTransient(ex))
        {
            if (DateTime.UtcNow >= startupDeadline)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Unable to reach queue '{queueName}' after {startupTimeoutSeconds}s.");
                Console.Error.WriteLine($"Last error: {ex.Reason} - {ex.Message}");
                Console.Error.WriteLine("Check that the emulator is running, the queue exists, and the connection string points to the emulator or Azure endpoint you intended.");
                Environment.ExitCode = 1;
                return;
            }

            Console.Error.WriteLine($"Transient Service Bus error ({ex.Reason}); retrying in {retryDelaySeconds}s...");
            await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
        }
    }
}

static string GetConfiguration(string key, string fallback)
{
    var value = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}

static bool IsTransient(ServiceBusException exception)
{
    return exception.Reason is ServiceBusFailureReason.ServiceCommunicationProblem
        or ServiceBusFailureReason.ServiceTimeout
        or ServiceBusFailureReason.MessagingEntityNotFound
        or ServiceBusFailureReason.GeneralError
        or ServiceBusFailureReason.ServiceBusy;
}
