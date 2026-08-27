# ServiceBusPeekTool

Service Bus queue inspector for local/debug use.

It uses `PeekMessagesAsync` to watch the queue non-destructively. Each message is printed
once when it arrives and marked `CONSUMED` when it disappears from the queue.

Press **Q**, **Esc**, or **Ctrl+C** to exit. On exit the tool drains and deletes all
remaining messages from the queue using `ReceiveAndDelete` mode.

## Run

```bash
cd ServiceBusPeekTool
dotnet run
```

## Commands

```bash
# Build
dotnet build

# Run with defaults
dotnet run

# Run against the Docker emulator explicitly
SERVICEBUS_CONNECTION='Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;' \
SERVICEBUS_QUEUE='signalr-fun-notifications' \
dotnet run

# Run against Azure Service Bus explicitly
SERVICEBUS_CONNECTION='Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<name>;SharedAccessKey=<key>' \
SERVICEBUS_QUEUE='signalr-fun-notifications' \
dotnet run
```

## Environment Variables

- `SERVICEBUS_CONNECTION`
Default: `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;`
- `SERVICEBUS_QUEUE`
Default: `signalr-fun-notifications`
- `SERVICEBUS_POLL_SECONDS`
Default: `2`
- `SERVICEBUS_MAX_MESSAGES`
Default: `10`
- `SERVICEBUS_BODY_LIMIT`
Default: `300`
- `SERVICEBUS_STARTUP_TIMEOUT_SECONDS`
Default: `120`
- `SERVICEBUS_RETRY_SECONDS`
Default: `3`

## Troubleshooting

- `MessagingEntityNotFound`
The queue does not exist in the endpoint you connected to. For the Docker emulator, recreate the emulator after changing `servicebus-emulator.config.json`, then verify the queue name is `signalr-fun-notifications`.
- `ServiceCommunicationProblem` or `tcp3 is closed`
The emulator is still starting or the AMQP endpoint is not ready yet. Wait until `docker logs servicebus-emulator` shows `Emulator Service is Successfully Up!`.
- `Unauthorized`
The connection string or endpoint does not match the target. For the emulator, use the local development emulator connection string. For Azure, use the namespace-level Service Bus connection string or AAD-based client.
- No output but the tool is running
There may simply be no messages in the queue. Send one message from the function app or a test producer and keep the function from consuming it while you inspect.
- Queue not cleared on exit
The drain uses `ReceiveAndDelete` with a 2-second wait per batch. If the emulator is slow to respond, some messages may remain. Re-run the tool and exit again to drain them.

## Startup Order

1. Start the emulator stack, from the repo root:

```bash
docker compose up -d
```

2. Wait for the emulator to finish booting:

```bash
docker logs -f servicebus-emulator
```

3. Run the peek tool:

```bash
dotnet run
```

If you want to inspect messages without the peek tool losing the race, stop `func start` first. The running function can consume the queue faster than the inspector can print it.
