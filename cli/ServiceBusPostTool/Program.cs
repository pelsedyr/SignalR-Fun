using System.Text.Json;
using Azure.Messaging.ServiceBus;

var connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = ResolveDefaultConnectionString();

var queueName = GetConfiguration("SERVICEBUS_QUEUE", "signalr-fun-notifications");
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
Console.WriteLine();

switch (PromptMode())
{
    case PostMode.Manual:
        await RunManualAsync(cancellationSource.Token);
        break;
    case PostMode.JokeStream:
        await RunJokeStreamAsync(cancellationSource.Token);
        break;
}

Console.WriteLine("Done.");

// Original behavior: a Receiver ID / Content pair per message, blank input exits.
async Task RunManualAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("Enter a Receiver ID and Content for each message. Leave Receiver ID blank, or press Ctrl+C, to exit.");
    Console.WriteLine();

    while (!cancellationToken.IsCancellationRequested)
    {
        Console.Write("Receiver ID: ");
        var receiverId = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(receiverId) || cancellationToken.IsCancellationRequested)
            break;

        Console.Write("Content:     ");
        var content = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(content) || cancellationToken.IsCancellationRequested)
            break;

        await SendNotificationAsync(new NotificationDto(receiverId, content), cancellationToken);
        Console.WriteLine();
    }
}

// Streams lines from jokes.txt to a single receiver on a fixed interval.
async Task RunJokeStreamAsync(CancellationToken cancellationToken)
{
    var jokesPath = FindJokesFile();
    if (jokesPath is null)
    {
        Console.Error.WriteLine("Could not find a joke list.");
        Console.Error.WriteLine("Looked for jokes.txt and test/jokes.txt in the current directory and its parents.");
        Console.Error.WriteLine("Set JOKES_FILE to point at one instead: a text file with one message per line.");
        Environment.ExitCode = 1;
        return;
    }

    var jokes = File.ReadAllLines(jokesPath)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToArray();

    if (jokes.Length == 0)
    {
        Console.Error.WriteLine($"'{jokesPath}' has no non-blank lines.");
        Environment.ExitCode = 1;
        return;
    }

    // Random order, so even a short run gets a mix from the whole file rather than
    // marching through it from the top.
    Random.Shared.Shuffle(jokes);

    Console.WriteLine($"Loaded {jokes.Length} jokes from {jokesPath}, shuffled.");
    Console.WriteLine();

    var receiverId = PromptWithDefault("Receiver ID", "bursdag");
    if (receiverId is null)
        return;

    var intervalSeconds = PromptInteger("Interval in seconds", 5, minimum: 0);
    if (intervalSeconds is null)
        return;

    var count = PromptInteger("How many messages", jokes.Length, minimum: 1);
    if (count is null)
        return;

    if (count > jokes.Length)
        Console.WriteLine($"Only {jokes.Length} jokes available; the list reshuffles and repeats after that.");

    Console.WriteLine();
    Console.WriteLine($"Sending {count} joke(s) in random order to '{receiverId}' every {intervalSeconds}s. Press Ctrl+C to stop early.");
    Console.WriteLine();

    for (var index = 0; index < count && !cancellationToken.IsCancellationRequested; index++)
    {
        var position = index % jokes.Length;

        // Every joke goes out once before any repeats; a new shuffle starts each pass.
        if (index > 0 && position == 0)
            Random.Shared.Shuffle(jokes);

        await SendNotificationAsync(new NotificationDto(receiverId, jokes[position]), cancellationToken);

        if (index == count - 1 || intervalSeconds == 0)
            continue;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds.Value), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    Console.WriteLine();
}

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

static PostMode PromptMode()
{
    while (true)
    {
        Console.WriteLine("How do you want to send?");
        Console.WriteLine("  [1] Manual      - enter a Receiver ID and Content per message");
        Console.WriteLine("  [2] Joke stream - send jokes from jokes.txt to one receiver on an interval");
        Console.Write("Choice [1]: ");

        var answer = Console.ReadLine();
        if (answer is null)
            return PostMode.Exit;

        switch (answer.Trim().ToLowerInvariant())
        {
            case "":
            case "1":
            case "m":
            case "manual":
                Console.WriteLine();
                return PostMode.Manual;
            case "2":
            case "j":
            case "joke":
            case "jokes":
                Console.WriteLine();
                return PostMode.JokeStream;
        }

        Console.WriteLine("Enter 1 or 2.");
        Console.WriteLine();
    }
}

// Returns null on end of input, so piped runs stop instead of looping on a re-prompt.
static string? PromptWithDefault(string label, string fallback)
{
    Console.Write($"{label} [{fallback}]: ");
    var value = Console.ReadLine();
    if (value is null)
        return null;

    value = value.Trim();
    return value.Length == 0 ? fallback : value;
}

static int? PromptInteger(string label, int fallback, int minimum)
{
    while (true)
    {
        var raw = PromptWithDefault(label, fallback.ToString());
        if (raw is null)
            return null;

        if (int.TryParse(raw, out var value) && value >= minimum)
            return value;

        Console.WriteLine($"Enter a whole number of {minimum} or more.");
    }
}

// jokes.txt lives in test/ next to the project, so check both that and the directory itself
// on the way up -- the tool is run from the project directory or the repo root.
static string? FindJokesFile()
{
    var configured = Environment.GetEnvironmentVariable("JOKES_FILE");
    if (!string.IsNullOrWhiteSpace(configured))
        return File.Exists(configured) ? configured : null;

    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    for (var depth = 0; dir is not null && depth < 6; depth++, dir = dir.Parent)
    {
        string[] candidates =
        [
            Path.Combine(dir.FullName, "jokes.txt"),
            Path.Combine(dir.FullName, "test", "jokes.txt")
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
    }

    return null;
}

static string GetConfiguration(string key, string fallback)
{
    var value = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}

// No SERVICEBUS_CONNECTION override: default to the local emulator, unless .env.registry
// declares SERVICEBUS_HOST for an externally-exposed one -- in which case ask, rather than
// silently switching away from localhost.
static string ResolveDefaultConnectionString()
{
    const string localhost =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    var host = FindServiceBusHostFromEnvRegistry();
    if (string.IsNullOrWhiteSpace(host))
    {
        Console.WriteLine("Using the local Service Bus emulator (sb://localhost).");
        Console.WriteLine("Tip: set SERVICEBUS_HOST in .env.registry to connect to an externally-exposed emulator instead.");
        Console.WriteLine();
        return localhost;
    }

    Console.Write($"Found SERVICEBUS_HOST='{host}' in .env.registry. Use it instead of localhost? [Y/n] ");
    var answer = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(answer) && !answer.Equals("y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Using the local Service Bus emulator (sb://localhost).");
        Console.WriteLine();
        return localhost;
    }

    Console.WriteLine($"Using external Service Bus host '{host}'.");
    Console.WriteLine();
    return $"Endpoint=sb://{host};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
}

// Walks up from the current directory looking for .env.registry, so this works whether the
// tool is run from the repo root or from cli/ServiceBusPostTool (the documented workflow).
static string? FindServiceBusHostFromEnvRegistry()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    for (var depth = 0; dir is not null && depth < 6; depth++, dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, ".env.registry");
        if (File.Exists(candidate))
            return ReadEnvValue(candidate, "SERVICEBUS_HOST");
    }

    return null;
}

static string? ReadEnvValue(string path, string key)
{
    foreach (var rawLine in File.ReadLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        var separator = line.IndexOf('=');
        if (separator < 0)
            continue;

        if (!string.Equals(line[..separator].Trim(), key, StringComparison.Ordinal))
            continue;

        var value = line[(separator + 1)..].Trim();
        if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
            value = value[1..^1];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    return null;
}

static bool IsTransient(ServiceBusException exception)
{
    return exception.Reason is ServiceBusFailureReason.ServiceCommunicationProblem
        or ServiceBusFailureReason.ServiceTimeout
        or ServiceBusFailureReason.MessagingEntityNotFound
        or ServiceBusFailureReason.GeneralError
        or ServiceBusFailureReason.ServiceBusy;
}

enum PostMode
{
    Manual,
    JokeStream,
    Exit
}
