# SignalR-Fun

A local spike for push notifications: a message dropped on a Service Bus queue is picked
up by an Azure Function, which fans it out to a specific user's browser over SignalR.
Everything runs on emulators — no Azure subscription, no cloud resources.

```
ServiceBusPostTool ──▶ Service Bus emulator ──▶ MessageHub ──▶ SignalR emulator ──▶ MessageReceiver
     (CLI, host)          (container)          (container)      (container)     (container → browser)
```

`MessageHub` is the interesting part: a `ServiceBusTrigger` reads the queue, deserializes
a `NotificationDto`, and returns a `SignalRMessageAction` addressed to
`notification.ReceiverId`. The SignalR emulator delivers it only to the browser that
negotiated with that user id.

## Quick start

```bash
docker compose up -d --build
```

That's the whole stack — emulators, the function and the frontend.

Open http://localhost:5173, connect as `user-123`, then in another terminal:

```bash
cd cli/ServiceBusPostTool && dotnet run -- user-123 "hei"
```

The notification should appear in the browser within a second.

## What's in the box

| Path | What it is | How it runs |
|---|---|---|
| `MessageHub/` | Azure Functions v4, .NET 10 isolated worker. `Negotiate` (HTTP) + `MessageTrigger` (Service Bus → SignalR) | container, port 7071 |
| `MessageReceiver/` | React 19 + Vite SPA using `@microsoft/signalr` | container, port 5173 |
| `cli/ServiceBusPostTool/` | Posts `NotificationDto` messages onto the queue | host, `dotnet run` |
| `cli/ServiceBusPeekTool/` | Non-destructive queue watcher; drains on exit | host, `dotnet run` |
| `Dockerfile` | Builds the Azure SignalR emulator image | container, port 8888 |
| `servicebus-emulator.config.json` | Declares the `d-avdekl-notifications` queue | mounted into the emulator |
| `signalr-emulator.settings.json` | Upstream webhook template | mounted into the emulator |

Ports: `5173` frontend · `7071` function · `8888` SignalR emulator · `5672`/`5300`
Service Bus emulator · `10000-10002` Azurite · `1433` MSSQL (backing store for the
Service Bus emulator).

## Prerequisites

Docker with Compose v2.22+ (for `docker compose watch`) and the .NET 10 SDK for the CLI
tools. Node 20+ and Azure Functions Core Tools are only needed if you want to run the
frontend or the function on the host instead of in a container — see below.

## The dev loop

```bash
docker compose watch
```

Leave that running. It covers both code services, but they behave differently on save
because their reload stories are different:

| Save a file in | What happens | Turnaround |
|---|---|---|
| `MessageReceiver/` | files sync into the running container, Vite hot-reloads | sub-second |
| `MessageHub/` | image rebuilds, container is recreated | ~26 s |

Ignores are set up so pointless work doesn't happen: `node_modules/` and `dist/` for the
frontend, `bin/`, `obj/`, `.vscode/` and `local.settings.json` for the function. Changing
`MessageReceiver/package.json` or `package-lock.json` is the one frontend edit that *does*
force a rebuild, since dependencies live in the image.

To rebuild on demand instead:

```bash
docker compose up -d --build message-hub
docker compose logs -f message-hub
```

**`docker compose watch` only watches `./MessageHub` and `./MessageReceiver`.** Changes to
`docker-compose.yaml` itself — ports, env vars, connection strings — are *not* picked up.
Apply those with `docker compose up -d`.

### Running either one on the host instead

Both containers publish the same port their host tooling uses — 7071 for `func start`,
5173 for `npm run dev` — so the SignalR emulator's upstream, the `/api` proxy and the
editor tasks don't care which side is running. Stop the container to free the port:

```bash
docker compose stop message-hub      && cd MessageHub      && func start
docker compose stop message-receiver && cd MessageReceiver && npm run dev
```

Worth doing for MessageHub when you're iterating hard or want a debugger attached — the
`func start` loop is a few seconds versus ~26 for a container rebuild. Rarely worth it for
the frontend, since the container already hot-reloads.

**Watch out:** `func start` fails loudly if 7071 is taken, but Vite does *not* — it prints
`Port 5173 is in use, trying another one...` and quietly moves to **5174**. If the page
looks stale, check whether the container is still holding 5173.

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

The frontend has one setting of the same shape, `API_PROXY_TARGET`, read by
`MessageReceiver/vite.config.js`:

| | Host (`npm run dev`) | Container |
|---|---|---|
| `API_PROXY_TARGET` | unset → falls back to `http://localhost:7071` | `http://host.docker.internal:7071` |

Inside a container `localhost` is the container itself, so the proxy has to reach out to
the host. Going straight to `message-hub:80` over the compose network would be one hop
shorter, but it would break as soon as you stop the container and run `func start` — the
hairpin through host port 7071 works either way. Note the name is deliberately *not*
`VITE_`-prefixed, which keeps it server-side and out of the client bundle.

## Handy commands

```bash
docker compose logs -f message-hub                        # function logs
docker compose logs -f message-receiver                   # Vite output, incl. HMR updates
docker compose ps                                         # what's up
cd cli/ServiceBusPeekTool && dotnet run                    # watch the queue (Q to quit + drain)
cd cli/ServiceBusPostTool && dotnet run                    # interactive post mode
curl -sX POST 'http://localhost:5173/api/negotiate?userId=user-123'   # tests the whole proxy chain
docker compose down                                       # stop everything
```

Both CLI tools read `SERVICEBUS_CONNECTION` and `SERVICEBUS_QUEUE` from the environment and
default to the emulator and `d-avdekl-notifications`.

## Recent changes

### MessageReceiver is now containerized too

The frontend was the last host-only piece, so the quick start needed two commands and a
`npm install`. It's now a `message-receiver` service and `docker compose up -d --build`
brings up the whole thing.

- **`MessageReceiver/Dockerfile`** — `node:24-slim` running the Vite dev server. Debian
  rather than Alpine on purpose: `package-lock.json` pins glibc-variant native bindings
  (`@rollup/rollup-linux-x64-gnu`, oxlint's `-gnu` bindings), and matching the libc avoids
  a whole class of `npm ci` platform failures for no meaningful size win.
- **`--host 0.0.0.0` passed on the command line**, not set in `vite.config.js`. Vite binds
  loopback by default, which is unreachable from outside the container — but the host
  workflow shouldn't start exposing itself to the LAN as a side effect.
- **`API_PROXY_TARGET`** in `vite.config.js` (see Configuration above) — the only source
  change this needed.
- **Port `5173:5173`, mapped 1:1 deliberately.** Vite's HMR client connects back to the
  same host:port the page was served from, so remapping would break hot reload.

**This is shaped differently from MessageHub, and that's the point.** MessageHub uses a
production-style image and rebuilds on every save because in-container `dotnet watch`
crashed the host (below). Vite has none of that problem — it has real HMR — so the
frontend uses `action: sync` instead of `action: rebuild`, and only `package.json` /
`package-lock.json` changes produce a new image. Copying MessageHub's approach here would
have turned a sub-second reload into a ~20 s rebuild, which would have been a downgrade
from running it on the host.

Verified end to end: editing `App.jsx` under `docker compose watch` logs
`Syncing service "message-receiver"` and `[vite] (client) hmr update /src/App.jsx` with no
rebuild, while touching `package.json` rebuilds `message-receiver` alone; a Service Bus
message still lands in a SignalR client that negotiated through the container's `/api`
proxy.

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
