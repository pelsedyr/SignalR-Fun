# SignalR-Fun

A local spike for push notifications: a message dropped on a Service Bus queue is picked
up by an Azure Function, which fans it out to a specific user's browser over SignalR.
Everything runs on emulators — no Azure subscription, no cloud resources.

```
ServiceBusPostTool ──▶ Service Bus emulator ──▶ MessageHub ──▶ SignalR emulator ──▶ MessageReceiver
     (CLI, host)          (container)          (container)      (container)        (browser)
```

`MessageHub` is the interesting part: a `ServiceBusTrigger` reads the queue, deserializes
a `NotificationDto`, and returns a `SignalRMessageAction` addressed to
`notification.ReceiverId`. The SignalR emulator delivers it only to the browser that
negotiated with that user id.

## Quick start

```bash
docker compose up -d --build          # emulators + the function
cd MessageReceiver && npm install && npm run dev
```

Open http://localhost:5173, connect as `user-123`, then in another terminal:

```bash
cd cli/ServiceBusPostTool && dotnet run -- user-123 "hei"
```

The notification should appear in the browser within a second.

## What's in the box

| Path | What it is | How it runs |
|---|---|---|
| `MessageHub/` | Azure Functions v4, .NET 10 isolated worker. `Negotiate` (HTTP) + `MessageTrigger` (Service Bus → SignalR) | container, port 7071 |
| `MessageReceiver/` | React 19 + Vite SPA using `@microsoft/signalr` | host, `npm run dev`, port 5173 |
| `cli/ServiceBusPostTool/` | Posts `NotificationDto` messages onto the queue | host, `dotnet run` |
| `cli/ServiceBusPeekTool/` | Non-destructive queue watcher; drains on exit | host, `dotnet run` |
| `Dockerfile` | Builds the Azure SignalR emulator image | container, port 8888 |
| `servicebus-emulator.config.json` | Declares the `d-avdekl-notifications` queue | mounted into the emulator |
| `signalr-emulator.settings.json` | Upstream webhook template | mounted into the emulator |

Ports: `7071` function · `8888` SignalR emulator · `5672`/`5300` Service Bus emulator ·
`10000-10002` Azurite · `1433` MSSQL (backing store for the Service Bus emulator).

## Prerequisites

Docker with Compose v2.22+ (for `docker compose watch`), .NET 10 SDK and Node 20+ for the
host-side pieces. Azure Functions Core Tools only if you want to run the function on the
host — see below.

## Working on MessageHub

```bash
docker compose watch
```

Leave that running. Saving any file under `MessageHub/` rebuilds the image and recreates
the container — measured at **~26 seconds from save to a serving endpoint**. `bin/`,
`obj/`, `.vscode/` and `local.settings.json` are ignored, so editing those doesn't trigger
a pointless rebuild.

To rebuild on demand instead:

```bash
docker compose up -d --build message-hub
docker compose logs -f message-hub
```

**`docker compose watch` only watches `./MessageHub`.** Changes to `docker-compose.yaml`
itself — ports, env vars, connection strings — are *not* picked up. Apply those with
`docker compose up -d`.

### Running it on the host instead

The container publishes port **7071**, the same port `func start` uses, so the SignalR
emulator's upstream, the Vite `/api` proxy and the editor tasks don't care which of the
two is running. They can't both hold the port, so stop the container first:

```bash
docker compose stop message-hub
cd MessageHub && func start
```

This is worth doing when you're iterating hard on the function or want a debugger
attached — the `func start` loop is a few seconds versus ~26 for a container rebuild.

## Configuration

The same six settings exist in two places, depending on how MessageHub runs:

| | Host (`func start`) | Container |
|---|---|---|
| Source | `MessageHub/local.settings.json` | `environment:` block in `docker-compose.yaml` |
| Tracked in git | no (gitignored) | yes |
| Hostnames | `localhost` | compose service names |

Two container-only details that are easy to get wrong:

- **`ClientEndpoint`** in `AzureSignalRConnectionString`. `Endpoint` is how the *function*
  reaches the emulator (`signalr-emulator:8888`); `ClientEndpoint` is what `negotiate`
  hands the *browser*, which can't resolve compose service names. Without it the SPA gets
  a URL it cannot connect to.
- **`__` instead of `:`** in `Email__ServiceBus__DefaultQueue` and `Notifications__HubName`.
  .NET config maps `__` → `:` when reading environment variables, which is what the
  `%Email:ServiceBus:DefaultQueue%` binding expressions resolve against.

Keep the two sides in sync when you add a setting.

## Handy commands

```bash
docker compose logs -f message-hub                        # function logs
docker compose ps                                         # what's up
cd cli/ServiceBusPeekTool && dotnet run                    # watch the queue (Q to quit + drain)
cd cli/ServiceBusPostTool && dotnet run                    # interactive post mode
curl -sX POST 'http://localhost:7071/api/negotiate?userId=user-123'
docker compose down                                       # stop everything
```

Both CLI tools read `SERVICEBUS_CONNECTION` and `SERVICEBUS_QUEUE` from the environment and
default to the emulator and `d-avdekl-notifications`.

## Recent changes

### MessageHub is now containerized

Previously MessageHub was the only component *not* in Compose — it ran on the host via
`func start` while everything else was a container, so a fresh clone couldn't bring the
stack up with one command. It's now a `message-hub` service.

- **`MessageHub/Dockerfile`** — multi-stage: `dotnet/sdk:10.0` builds and publishes, then
  the artifacts are copied into `azure-functions/dotnet-isolated:4-dotnet-isolated10.0`,
  the same host image Azure runs. Final image **1.29 GB**. BuildKit cache mounts keep the
  generated `WorkerExtensions` sub-project (which targets `net8.0`) from re-downloading
  the 8.0 reference pack on every rebuild.
- **`develop.watch` with `action: rebuild`** for the inner loop, rather than a
  bind-mounted source tree.
- **`MessageHub/docker-entrypoint.sh`** — waits for the Service Bus emulator's AMQP port
  before starting the Functions host, then `exec`s the base image's startup script. The
  wait is bounded at 60 s and never fatal. Overridable via `SERVICEBUS_WAIT_HOST` /
  `SERVICEBUS_WAIT_PORT`.
- **Port `7071:80`** so the SignalR upstream, the Vite proxy and both editor task files
  keep working with no edits, and host mode remains a `docker compose stop` away.

Two decisions worth recording, both driven by things that were measured rather than
assumed:

**No `dotnet watch` inside the container.** An earlier attempt bind-mounted the source into
an SDK image with Core Tools and ran `dotnet watch` alongside `func start`. Every rebuild
re-registered the functions into the still-running host:

```
System.InvalidOperationException: Unable to load Function 'MessageTrigger'.
A function with the id '1853734259' name already exists.
```

That image was 4.64 GB and the container ended up killed. Rebuilding the image and
recreating the container is slower per iteration but always starts from a clean host.

**The startup wait replaced a restart policy that was doing nothing.** The Service Bus
emulator waits on MSSQL and needs ~10 s before it accepts AMQP. The Functions host doesn't
*exit* when a listener can't connect — it retries internally — so `restart:` never fired.
Cold starts logged **1536** `Connection refused` stack traces before settling. With the
entrypoint wait that is **0**, and `restart:` is now `on-failure`, honestly scoped as a
safety net for a genuine crash. The emulator image has no shell, so a Compose healthcheck
on it isn't possible; the wait has to live on the consumer side.

### Known rough edges

- The SignalR emulator's upstream webhook is effectively inert: there's no `SignalRTrigger`
  in this app, and the output binding talks to the emulator directly over REST. It may
  return 401 in container mode (Core Tools disables key auth, the real host doesn't). It
  doesn't matter today, but it would if a `SignalRTrigger` is ever added.
- `cli/*/README.md` reference a `docker-compose.dev.yaml` and a `d-avdekl-email` queue that
  don't exist.
- `MessageHub/Properties/launchSettings.json` says port 7232 while everything else assumes
  7071.
- `NotificationDto` is duplicated between `MessageHub/` and `cli/ServiceBusPostTool/`.
