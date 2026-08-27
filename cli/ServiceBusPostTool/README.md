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
Default: none — see "Connecting to an external emulator" below for what's used instead.
- `SERVICEBUS_QUEUE`
Default: `signalr-fun-notifications`
- `SERVICEBUS_STARTUP_TIMEOUT_SECONDS`
Default: `120`
- `SERVICEBUS_RETRY_SECONDS`
Default: `3`

## Connecting to an external emulator

If `SERVICEBUS_CONNECTION` isn't set, the tool looks for `.env.registry` (checked in the
current directory and its parents — see the root README's "Configuring where images live")
for a `SERVICEBUS_HOST` value, e.g. one exposed through a reverse proxy in front of a registry
deployment. If found, it asks before using it:

```
Found SERVICEBUS_HOST='servicebus.example.net' in .env.registry. Use it instead of localhost? [Y/n]
```

Press Enter or `y` to connect to that host instead of `sb://localhost` (same fixed
emulator credentials, different endpoint); anything else falls back to localhost. If
`SERVICEBUS_HOST` isn't set at all, the tool goes straight to localhost and prints a one-line
tip about the option. Setting `SERVICEBUS_CONNECTION` explicitly always skips this prompt.

## Troubleshooting

See [ServiceBusPeekTool's README](../ServiceBusPeekTool/README.md#troubleshooting) — this tool
uses the same connection/queue defaults and the same transient-error retry behavior while the
emulator is starting up.

## Startup Order

1. Start the emulator stack, from the repo root:

```bash
docker compose up -d
```

2. Wait for the emulator to finish booting:

```bash
docker logs -f servicebus-emulator
```

3. Run this tool to send messages, and `ServiceBusPeekTool` in another terminal to watch them arrive.
