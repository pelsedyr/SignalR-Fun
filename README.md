# SignalR-Fun

A local spike for push notifications: a message dropped on a Service Bus queue is picked
up by an Azure Function, which fans it out to a specific user's browser over SignalR.
Everything runs on emulators — no Azure subscription, no cloud resources.

```
ServiceBusPostTool ──▶ Service Bus emulator ──▶ MessageHub ──▶ Cosmos DB emulator
     (CLI, host)          (container)          (container)        (container)
                                                    │                   ▲
                                          nudge ────┤                   │ REST backfill
                                                    ▼                   │
                                            SignalR emulator ──▶ MessageReceiver
                                               (container)     (container → browser)
```

`MessageHub` is the interesting part: a `ServiceBusTrigger` reads the queue, writes a
`NotificationDocument` to Cosmos, then returns a `SignalRMessageAction` addressed to
`notification.ReceiverId`.

**Cosmos is the inbox; SignalR only nudges.** The push carries `{ id, createdUtc }` and no
content — the browser re-reads the list from `GET /api/notifications`. That is what makes
notifications survive a disconnected recipient, a page refresh, or a second tab: Azure SignalR
drops messages aimed at a user with no live connection, so anything that treats it as the
delivery guarantee loses them silently.

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

## Registry deployment

`docker-compose.registry.yaml` is intentionally separate from the development stack. It
pulls pre-built images, serves the React bundle with Nginx instead of Vite, and exposes no
application or emulator ports. It requires the public, HTTPS URL that a browser should use
for the SignalR hub; Compose refuses to start if the value is absent.

Build and push the three application images from a build machine by running
`build-and-push-to-registry.sh` from the repo root:

```bash
./build-and-push-to-registry.sh
```

It prompts for the registry URL and image tag, builds `signalr-emulator`, `message-hub`, and
`message-receiver` (the latter via `MessageReceiver/Dockerfile.production`), and pushes all
three. For non-interactive use, supply `REGISTRY`/`TAG` in the environment instead:

```bash
REGISTRY=registry.example.net/registry/signalr-fun TAG=1.0.0 ./build-and-push-to-registry.sh
```

On the deployment host, the `signalr-fun` network is external, so create it once before the
first `up` (this keeps it alive independent of either Compose project's lifecycle):

```bash
docker network create signalr-fun
```

Then deploy with the public SignalR hostname supplied by the shell (or an uncommitted
`--env-file`):

```bash
SIGNALR_CLIENT_ENDPOINT=https://signalr-notifications.example.net \
IMAGE_PREFIX=registry.example.net/registry/signalr-fun IMAGE_TAG=1.0.0 \
docker compose -f docker-compose.registry.yaml pull

SIGNALR_CLIENT_ENDPOINT=https://signalr-notifications.example.net \
IMAGE_PREFIX=registry.example.net/registry/signalr-fun IMAGE_TAG=1.0.0 \
docker compose -f docker-compose.registry.yaml up -d
```

If Nginx Proxy Manager runs in another Compose project on the same Docker host, declare
`signalr-fun` as an `external: true` network there too and attach its container to
it. Configure proxy hosts for `message-receiver:80` (the web application) and
`signalr-emulator:8888` (with WebSocket support). The first host handles `/api` internally
through the frontend Nginx, so it does not need a separate public route to MessageHub.
The second host's public URL is the value to use for `SIGNALR_CLIENT_ENDPOINT` above — the
browser connects to it directly for the live SignalR connection, bypassing MessageHub and the
frontend entirely once negotiate hands back that address.

If Nginx Proxy Manager instead runs on a *different* host and can't join the `signalr-fun`
network, it can't reach the containers by name. Publish their ports to the deployment host
instead by layering `docker-compose.registry.local.yml` on top of the `up`/`pull` commands
above (despite the "local" name, this is the supported way to expose them to an external
reverse proxy), then point Nginx Proxy Manager's proxy hosts at the deployment host's IP on
port `8080` (frontend) and port `8888` (SignalR emulator) instead of the container names.

### Testing pulled images locally

`docker-compose.registry.yaml` publishes no ports on its own, since a real deployment reaches
it through Nginx Proxy Manager on the shared network instead. To try a pulled image on your
own machine, layer `docker-compose.registry.local.yml` on top — it publishes `8888` (SignalR
emulator) and `8080` (frontend) and is local-only; don't use it on the deployment host.

```bash
docker network create signalr-fun   # skip if it already exists

SIGNALR_CLIENT_ENDPOINT=http://localhost:8888 \
IMAGE_PREFIX=registry.example.net/registry/signalr-fun IMAGE_TAG=1.0.0 \
docker compose -f docker-compose.registry.yaml pull

SIGNALR_CLIENT_ENDPOINT=http://localhost:8888 \
IMAGE_PREFIX=registry.example.net/registry/signalr-fun IMAGE_TAG=1.0.0 \
docker compose -f docker-compose.registry.yaml -f docker-compose.registry.local.yml up -d
```

Open http://localhost:8080. Tear down with the same `-f` flags plus `down` (add `-v` to also
drop the `cosmos-data` volume it created).

## What's in the box

| Path | What it is | How it runs |
|---|---|---|
| `MessageHub/` | Azure Functions v4, .NET 10 isolated worker. `Negotiate`, `GetNotifications`, `MarkNotificationRead` (HTTP) + `ServiceBusQueueTrigger` (Service Bus → Cosmos → SignalR) | container, port 7071 |
| `MessageReceiver/` | React 19 + Vite SPA using `@microsoft/signalr`; renders from the REST API, nudged by SignalR | container, port 5173 |
| `cli/ServiceBusPostTool/` | Posts `NotificationDto` messages onto the queue | host, `dotnet run` |
| `cli/ServiceBusPeekTool/` | Non-destructive queue watcher; drains on exit | host, `dotnet run` |
| `Dockerfile` | Builds the Azure SignalR emulator image | container, port 8888 |
| `servicebus-emulator.config.json` | Declares the `d-avdekl-notifications` queue | mounted into the emulator |
| `signalr-emulator.settings.json` | Upstream webhook template | mounted into the emulator |

Ports: `5173` frontend · `7071` function · `8888` SignalR emulator · `8081` Cosmos gateway ·
`1234` Cosmos data explorer · `5672`/`5300` Service Bus emulator · `10000-10002` Azurite ·
`1433` MSSQL (backing store for the Service Bus emulator).

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

The same settings exist in two places, depending on how MessageHub runs:

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

A third that bites the same way, added with Cosmos:

- **`ConnectionMode.Gateway`** in `Program.cs` — the emulator doesn't serve the Direct-mode
  backend protocol. `LimitToEndpoint = true` sits alongside it to skip region discovery.
  Worth knowing: the emulator derives the endpoint it advertises in `writableLocations` from
  the request's `Host` header, so it already reports `cosmosdb-emulator:8081` to the container
  and `localhost:8081` to the host without any configuration. `GATEWAY_PUBLIC_ENDPOINT` exists
  to force a fixed value and is deliberately left unset — setting it to the compose service name
  would break the host `func start` fallback.

Cosmos settings, following the same host/container split:

| | Host (`func start`) | Container |
|---|---|---|
| `CosmosConnection` | `AccountEndpoint=http://localhost:8081/;…` | `AccountEndpoint=http://cosmosdb-emulator:8081/;…` |
| `Cosmos__DatabaseId` | `log` | `log` |
| `Cosmos__ContainerId` | `notifications` | `notifications` |

The gateway speaks plain **HTTP**, so there is no certificate to import — unlike the classic
Cosmos emulator. The database and container are created at startup by `CosmosBootstrapper`,
so a fresh volume needs no manual setup.

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

## API

All four functions are HTTP-anonymous and served under `/api` on port `7071`. The frontend
reaches them through the Vite proxy on `5173`, so either origin works.

`userId` is a plain query parameter throughout, matching the shortcut in `Negotiate.cs`. A real
deployment derives it from an authenticated claim — until then, anyone can pass any id.

### `POST|GET /api/negotiate`

Mints a SignalR connection token scoped to a user. The browser calls this once, then connects
directly to the SignalR service.

| Param | In | Required | Notes |
|---|---|---|---|
| `userId` | query | yes | Becomes the SignalR user id the nudge is addressed to |

```bash
curl -sX POST 'http://localhost:7071/api/negotiate?userId=user-123'
# 200 → {"url":"http://localhost:8888/client/?hub=notifications","accessToken":"…"}
```

### `GET /api/notifications`

The inbox. This is the **only** source of rendered notification data — SignalR never carries it.

| Param | In | Required | Notes |
|---|---|---|---|
| `userId` | query | yes | Cosmos partition key; scopes the read to one partition |
| `unread` | query | no | `true` returns only items with no `readUtc`. Anything else returns all |

Returns newest-first (`ORDER BY c.createdUtc DESC`).

```bash
curl -s 'http://localhost:7071/api/notifications?userId=user-123'
```

```json
[
  {
    "id": "d2032a85-f1b0-4b7d-a42b-6037b48dbc5a",
    "receiverId": "user-123",
    "content": "sendt mens frakoblet",
    "createdUtc": "2026-08-25T09:53:35.867+00:00",
    "readUtc": null
  }
]
```

| Status | When |
|---|---|
| `200` | Always on success — an unknown `userId` returns `[]`, not `404` |
| `400` | `userId` missing → `{"error":"userId is required."}` |

### `POST /api/notifications/{id}/read`

Marks one notification read by stamping `readUtc`.

| Param | In | Required | Notes |
|---|---|---|---|
| `id` | route | yes | The notification id |
| `userId` | query | yes | Partition key — required because this is a point write |

```bash
curl -sX POST 'http://localhost:7071/api/notifications/<id>/read?userId=user-123'
```

| Status | When |
|---|---|
| `204` | Marked read. **Idempotent** — repeating it is still `204` and does not move `readUtc` |
| `400` | `userId` missing |
| `404` | No notification with that id **in that user's partition** |

That `404` is the isolation boundary doing its job: `userId` is the partition key, so passing
another user's notification id returns `404` rather than marking it read. With no auth in the
spike, this is the only thing keeping users apart.

### SignalR event: `notificationReceived`

Pushed to the SignalR user id from `negotiate`. **A nudge, not the notification** — it carries no
content, and the client re-reads `GET /api/notifications` in response.

```json
{ "id": "4029eb4b-f889-4351-a116-b87c60235699", "createdUtc": "2026-08-25T10:02:38.965+00:00" }
```

The payload is deliberately ignored by `App.jsx`, which just debounces 300 ms and re-fetches.
Refetching unconditionally is what makes duplicate and out-of-order pushes harmless, and it also
picks up changes no nudge announced — a mark-as-read from another tab, for instance.

#### Offline recipients: the nudge is always *sent*, only online users *receive* it

`ServiceBusQueueTrigger` returns the `SignalRMessageAction` unconditionally — there is no online check
anywhere in the code path. Azure SignalR then looks up connections mapped to that user id:

| Recipient state | What happens |
|---|---|
| Has one or more connections | Delivered to all of them |
| No connections | **The service drops it silently** — no error, no callback, no retry, nothing logged |

**The Function cannot tell the difference, and reports success either way.** Posting to a user
with nothing connected anywhere:

```
Executed 'Functions.ServiceBusQueueTrigger' (Succeeded, Duration=14ms)
```

That is not a gap in the implementation — Azure SignalR offers no delivery confirmation for
`UserId`-targeted sends. There is nothing to check and nothing to retry.

**This is exactly what the design is built around.** The ordering is what matters:

```
persist to Cosmos   ← durable, always happens
       ↓
push the nudge      ← best-effort, may go nowhere
```

A dropped nudge costs nothing, because the notification is already durable. On the user's next
connect, `App.jsx` calls the backfill straight after `connection.start()` and picks up everything
missed — including items sent hours earlier. The live path can fail entirely without affecting
correctness.

Contrast with the pre-Cosmos code, where the push *was* the delivery: a user offline for ten
seconds lost the notification permanently, and every metric still said success.

**Don't gate on "is the user online".** Azure SignalR's Management SDK can check user existence
(`HEAD /api/v1/hubs/{hub}/users/{userId}`), but it is racy — the user can connect or disconnect
between the check and the send — so the Cosmos fallback is needed regardless. It would add a REST
round-trip and a new failure mode to avoid an operation that is already harmless and free.

## Handy commands

```bash
docker compose logs -f message-hub                        # function logs
docker compose logs -f message-receiver                   # Vite output, incl. HMR updates
docker compose ps                                         # what's up
cd cli/ServiceBusPeekTool && dotnet run                    # watch the queue (Q to quit + drain)
cd cli/ServiceBusPostTool && dotnet run                    # interactive post mode
curl -s 'http://localhost:5173/api/notifications?userId=user-123'      # the inbox, via the proxy
curl -sX POST 'http://localhost:5173/api/negotiate?userId=user-123'   # tests the whole proxy chain
curl -s http://localhost:8081/dbs/log/colls                            # Cosmos container + partition key
open http://localhost:1234                                             # Cosmos data explorer
docker compose down                                       # stop; notifications SURVIVE
docker compose down -v                                    # stop and wipe Cosmos data
```

**`docker compose down` no longer resets the data.** The Cosmos emulator has a named volume
(`cosmos-data`), so notifications persist across restarts — use `down -v` for a clean slate.
That volume holds a Postgres cluster tied to the emulator image's major version, so bumping the
pinned image tag may require a `down -v`.

Both CLI tools read `SERVICEBUS_CONNECTION` and `SERVICEBUS_QUEUE` from the environment and
default to the emulator and `d-avdekl-notifications`.

## Recent changes

### MessageHub reorganized to the Avdekl function-app structure

The project had grown to twelve `.cs` files flat in the root, all in one namespace. It now
follows the layout used by the sibling function apps in `ac-avdekl`:

```
Dto/  Models/  Repositories/  Functions/  Extensions/(Logger/)  Exceptions/  Static/
```

`Avdekl.Function.Email` was used as the template rather than `Avdekl.Function.Brreg`: Brreg is
HTTP-only with no persistence, while Email is the house pattern for exactly this shape — a
Service Bus queue trigger plus Cosmos repositories.

- **`INotificationStore` / `CosmosNotificationStore` → `Repositories/INotificationRepository` /
  `NotificationRepository`**, with the `(IConfiguration, CosmosClient)` constructor Email uses.
- **Logging is now source-generated `[LoggerMessage]` extensions** under `Extensions/Logger/`.
  Message text is unchanged, so existing log greps still work.
- **Config keys are unchanged** — `CosmosConnection`, `Cosmos:DatabaseId`, `Cosmos:ContainerId`
  keep their names and just move into `Static/Strings.ConfigurationKeys`. Renaming them to
  Email's `Notifications:Cosmos:*` form would have rippled into `docker-compose.yaml` and
  `local.settings.json` for no gain in a spike.
- **`CosmosBootstrapper` moved from an `IHostedService` to a static `EnsureProvisionedAsync`**
  called from `Program.cs`, mirroring Email's `EmailTemplateSeeder`.

**One rename is visible at runtime:** the Service Bus trigger is now `ServiceBusQueueTrigger`,
so logs read `Executed 'Functions.ServiceBusQueueTrigger'`. The three HTTP function names and
every route are unchanged.

**One thing that did not port cleanly.** Email's `CreateJsonResponse` extension writes the body
with the synchronous `response.WriteString(...)`, which is fine under its `HostBuilder` host.
This app uses `ConfigureFunctionsWebApplication()` (ASP.NET Core-integrated), where synchronous
IO throws `Synchronous operations are disallowed`. Every HTTP endpoint returned 500 until the
helper was made async — the same class of difference the comment in `NegotiateController`
already described. Worth knowing before copying other helpers across.

Verified as behaviour-preserving by capturing every endpoint's response before and after and
diffing: identical, including status codes and error bodies. The nudge payload, the
`CreateItemAsync`-not-upsert idempotency and `readUtc` preservation were all re-checked.

### Notifications are persisted in Cosmos DB; SignalR now only nudges

Previously the trigger pushed the whole notification over SignalR and nothing was stored, so
anything sent while the recipient was disconnected was lost, and a refresh emptied the list.
Cosmos is now the durable inbox.

- **`ServiceBusQueueTrigger`** writes a `NotificationDocument` (database `log`, container
  `notifications`, partition key `/receiverId`) and then returns a nudge — `{ id, createdUtc }`,
  no content.
- **New HTTP endpoints:** `GET /api/notifications?userId=&unread=` and
  `POST /api/notifications/{id}/read?userId=`.
- **The frontend renders from the API**, not from the push. It backfills on connect, debounces
  nudges 300 ms before re-fetching, and re-fetches on reconnect.

**Why a nudge and not the payload.** Pushing the whole notification means two code paths produce
notification objects — the SignalR payload and the REST response — and they must stay identical
forever. When they drift, a notification renders differently depending on whether the user was
online when it arrived. Since the REST path has to exist anyway for backfill, the nudge *removes*
an implementation rather than adding one. It also makes duplicate and out-of-order pushes
harmless: both just trigger a redundant fetch of the same authoritative list.

**Two bugs fixed on the way in.**

The trigger used to call `CompleteMessageAsync` *before* returning its output binding. The host
processes output bindings after the function body returns, so the message was settled while the
SignalR push could still fail — with nothing left to redeliver and no dead-letter record. The fix
was deletion: dropping the manual settlement and the `ServiceBusMessageActions` parameter lets
auto-completion settle only after the body *and* every output binding succeed. The runtime now
logs `AutoCompleteMessages … overriden to 'True'`, which is the correct behaviour here and also
resolves a latent conflict with the default `host.json`.

The Cosmos write uses `CreateItemAsync` and treats a 409 as success, **not** `UpsertItemAsync`.
Service Bus is at-least-once, so the write runs again on redelivery — and an upsert would
overwrite `readUtc` back to null, silently un-reading a notification the user had already read.
Verified by re-sending a processed `MessageId`: one document, original content, `readUtc` intact.

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
