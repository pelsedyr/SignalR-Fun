# ServiceBusPostTool

Service Bus message producer for local/debug use. Sends `NotificationDto` messages
(JSON-serialized) to a queue so [ServiceBusPeekTool](../ServiceBusPeekTool) has something to watch.

## Run

```bash
cd ServiceBusPostTool

# Interactive: prompts for Receiver ID / Content, sends one message per pair, exits on blank input
dotnet run

# One-shot: send a single message and exit
dotnet run -- user-123 "Hello from the post tool"
```

## Environment Variables

- `SERVICEBUS_CONNECTION`
Default: `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;`
- `SERVICEBUS_QUEUE`
Default: `d-avdekl-notifications`
- `SERVICEBUS_STARTUP_TIMEOUT_SECONDS`
Default: `120`
- `SERVICEBUS_RETRY_SECONDS`
Default: `3`

## Troubleshooting

See [ServiceBusPeekTool's README](../ServiceBusPeekTool/README.md#troubleshooting) — this tool
uses the same connection/queue defaults and the same transient-error retry behavior while the
emulator is starting up.

## Startup Order

1. Start the emulator stack:

```bash
docker compose -f docker-compose.dev.yaml up -d
```

2. Wait for the emulator to finish booting:

```bash
docker logs -f servicebus-emulator
```

3. Run this tool to send messages, and `ServiceBusPeekTool` in another terminal to watch them arrive.
