using Azure.Messaging.ServiceBus;

var connectionString = GetConfiguration(
    "SERVICEBUS_CONNECTION",
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

var queueName = GetConfiguration("SERVICEBUS_QUEUE", "signalr-fun-notifications");
var pollSeconds = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_POLL_SECONDS"), out var parsed)
    ? Math.Max(1, parsed)
    : 2;
var maxMessages = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_MAX_MESSAGES"), out parsed)
    ? Math.Max(1, parsed)
    : 10;
var bodyLimit = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_BODY_LIMIT"), out parsed)
    ? Math.Max(1, parsed)
    : 300;
var startupTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SERVICEBUS_STARTUP_TIMEOUT_SECONDS"), out parsed)
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

// Q or Escape also exits
var keyWatcher = Task.Run(async () =>
{
    while (!cancellationSource.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                cancellationSource.Cancel();
                break;
            }
        }
        await Task.Delay(100);
    }
});

await using var client = new ServiceBusClient(connectionString);
var receiver = client.CreateReceiver(queueName);

Console.WriteLine($"Peeking queue '{queueName}' every {pollSeconds}s.");
Console.WriteLine("Messages are not consumed while watching. On exit all remaining messages are deleted.");
Console.WriteLine("Press Q, Esc, or Ctrl+C to exit.");
Console.WriteLine();

var startupDeadline = DateTime.UtcNow.AddSeconds(startupTimeoutSeconds);
var seenIds = new HashSet<string>();

while (!cancellationSource.IsCancellationRequested)
{
    try
    {
        var messages = await receiver.PeekMessagesAsync(maxMessages, fromSequenceNumber: 1, cancellationToken: cancellationSource.Token);
        var currentIds = messages.Select(m => m.MessageId).ToHashSet();

        foreach (var message in messages)
        {
            if (!seenIds.Add(message.MessageId))
                continue;

            var body = message.Body.ToString();
            if (body.Length > bodyLimit)
                body = $"{body[..bodyLimit]}...";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss} ARRIVED  ] MessageId: {message.MessageId}");
            Console.WriteLine($"  Subject:  {message.Subject ?? "(null)"}");
            Console.WriteLine($"  Enqueued: {message.EnqueuedTime:O}");
            Console.WriteLine($"  Body:     {body}");
            Console.WriteLine(new string('-', 80));
        }

        foreach (var id in seenIds.Except(currentIds).ToList())
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss} CONSUMED ] MessageId: {id}");
            seenIds.Remove(id);
        }

        await Task.Delay(TimeSpan.FromSeconds(pollSeconds), cancellationSource.Token);
    }
    catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
    {
        break;
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
            break;
        }

        Console.Error.WriteLine($"Transient Service Bus error ({ex.Reason}); retrying in {retryDelaySeconds}s...");
        await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationSource.Token);
    }
}

await keyWatcher;

Console.WriteLine();
Console.WriteLine("Clearing queue...");
await using var drainReceiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
{
    ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
});

var cleared = 0;
while (true)
{
    var batch = await drainReceiver.ReceiveMessagesAsync(maxMessages: 50, maxWaitTime: TimeSpan.FromSeconds(2));
    if (batch.Count == 0)
        break;
    cleared += batch.Count;
}

Console.WriteLine(cleared > 0
    ? $"Cleared {cleared} message(s) from '{queueName}'."
    : $"Queue '{queueName}' was already empty.");

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
