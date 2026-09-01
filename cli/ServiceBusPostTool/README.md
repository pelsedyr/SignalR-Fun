# ServiceBusPostTool

Service Bus message producer for local/debug use. Sends `NotificationDto` messages
(JSON-serialized) to a queue so [ServiceBusPeekTool](../ServiceBusPeekTool) has something to watch.

## Run

```bash
cd ServiceBusPostTool

# Interactive: asks which mode to use, then prompts accordingly
dotnet run

# One-shot: send a single message and exit
dotnet run -- user-123 "Hello from the post tool"
```

Interactive mode asks how to send:

- **`[1]` Manual** (the default) — prompts for a Receiver ID / Content pair per message,
  sends one message per pair, exits on blank input.
- **`[2]` Joke stream** — prompts for a single Receiver ID, an interval, and a message count,
  then sends one line of `jokes.txt` per message on that interval. Ctrl+C stops it early.

The joke stream reads `jokes.txt` or `test/jokes.txt`, looked up in the current directory and
its parents (the list shipped with the repo lives in `test/jokes.txt`). Set `JOKES_FILE` to use
a different file — any text file with one message per line; blank lines are skipped. If the
requested count exceeds the number of lines, the list repeats from the start.

## Environment Variables

- `SERVICEBUS_CONNECTION`
Default: none — see "Connecting to an external emulator" below for what's used instead.
- `SERVICEBUS_QUEUE`
Default: `signalr-fun-notifications`
- `SERVICEBUS_STARTUP_TIMEOUT_SECONDS`
Default: `120`
- `SERVICEBUS_RETRY_SECONDS`
Default: `3`
- `JOKES_FILE`
Default: none — `jokes.txt` / `test/jokes.txt` is discovered by walking up from the current directory.

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
