# Operating Armada

Read this if you are the person who keeps an Armada install running: picking a deployment shape, knowing which ports
are open and why, protecting the data, keeping storage in check, and getting a stuck install moving again. It
assumes Armada is already installed. For first-time setup, start with [GETTING_STARTED.md](../GETTING_STARTED.md)
or [DOCKER.md](DOCKER.md); for moving between versions, read [UPGRADING.md](UPGRADING.md).

Most settings named below live in the Admiral's settings file. On a single box that is `~/.armada/settings.json`
(the data directory can be moved with the `ARMADA_DATA_DIR` environment variable). In the Docker stack it is
`docker/armada/armada.json`, mounted into the container at `/app/data/armada.json`. Changes to the settings file
take effect on the next server start.

## Deployment topologies

Armada has four shapes in practice. They differ mainly in where the agent CLIs run, because a captain needs the
code, the git credentials, and the agent's own login on the machine that launches it.

### Single box (Local mode)

The Admiral, the dashboard, and every captain run on one machine, usually the developer's own. This is the default
(`deploymentMode` is `Local`), it has the fewest moving parts, and it is the shape the README walks through. The
Admiral serves the REST API, WebSocket, and React dashboard on port 7890 and the MCP server on port 7891; captains
are launched as child processes in git worktrees under `~/.armada/docks`.

Pick this unless you have a reason not to. To keep it running across reboots, see
[RUN_ON_STARTUP.md](RUN_ON_STARTUP.md), which covers a Windows startup entry, a `systemd --user` unit, and a
`launchd` agent.

### Docker

`docker/armada/compose.yaml` runs the Admiral (`armada-server`), the standalone React dashboard (`armada-dashboard`,
nginx on host port 3000), and an observability stack (Prometheus, Loki, Grafana). Data is kept in bind mounts next to
the compose file: `docker/armada/db` for the SQLite database and `docker/armada/logs` for logs. The container
binds `0.0.0.0`, so the published ports are reachable from other hosts on the network.

Be clear about what the container can do: the server image does not include agent CLIs, so a containerized Admiral
in Local mode can schedule and track work but has nothing on board to run captains with. For real work from a
container, use split mode below. [DOCKER.md](DOCKER.md) covers volumes, building images, and factory reset;
`docker/update.sh` (or `docker/update.bat`) pulls the latest images and recreates the stack without touching
volumes.

### Split mode with a Harbor (experimental)

Split mode is marked **experimental** for 1.0 and is excluded from the compatibility promise. It works, but its
transport and settings may still change.

In split mode the Admiral runs detached (typically in Docker via `docker/armada/compose.split.yaml`) and a Harbor
runs on each machine where code and agent logins live. The Harbor dials out to the Admiral over a WebSocket on the
Admiral port at `/v1.0/harbor/connect`, receives host work (git, worktrees, captain launches), and streams results
back. Captains it launches call home to the MCP URL the Admiral advertises in `harbor.advertisedMcpBaseUrl`. The
split profile sets `deploymentMode: "Split"` and `requireHarborForLaunch: true`, so a mission waits until an eligible
Harbor owned by the requesting user is connected rather than running inside the container.

Two operational details matter here. Harbor link authentication is off by default (`harbor.requireAuth` is
`false`), so a split Admiral should not be reachable from untrusted networks. And the advertised MCP URL must be
reachable from the Harbor host, not from inside the container. [HARBOR.md](HARBOR.md) explains the model and
[HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md) the wire contract.

### Remote access through Armada.Proxy

`Armada.Proxy` lets you reach an Admiral that sits behind NAT without opening inbound ports to it. The Admiral opens
an outbound tunnel (`remoteControl.enabled`, `remoteControl.tunnelUrl`) to the proxy, and the proxy serves a portal
and a relayed dashboard on port 7893. The relay deliberately blocks or write-blocks some administrative routes; see
[PROXY_API.md](PROXY_API.md) for the policy. Setup is in [REMOTE_MGMT.md](REMOTE_MGMT.md) and day-two operations,
including the common failure modes, are in [TUNNEL_OPERATIONS.md](TUNNEL_OPERATIONS.md). The proxy and the tunnel
share one password. The proxy refuses to start while that password is blank or the built-in default `armadaadmin`
(set `password` in `proxysettings.json` or `ARMADA_PROXY_PASSWORD`; `allowDefaultPassword` overrides this for a local
test only), and the Admiral logs a warning when `remoteControl.password` is still the default. Proxy logins and tunnel
handshakes are rate limited per client address (429 with `Retry-After`).

## Ports

Every port below is configurable. The table shows defaults and the process that binds each one.

| Port | Bound by | Purpose | Setting |
|------|----------|---------|---------|
| 7890 | Admiral | REST API, OpenAPI (`/openapi.json`), React dashboard at `/dashboard`, WebSocket at `/ws`, Harbor link at `/v1.0/harbor/connect` | `admiralPort`, `rest.hostname` |
| 7891 | Admiral | MCP server (`/mcp`) used by orchestrators and by captains calling home | `mcpPort` (binds the same `rest.hostname`) |
| 9464 | Admiral | Prometheus scrape endpoint (`/metrics`), only when telemetry is enabled | `telemetry.prometheusPort` |
| 7893 | Armada.Proxy | Proxy portal, relayed dashboard, tunnel endpoint at `/tunnel` | `ArmadaProxy.port` |
| 3000 | Docker `armada-dashboard` | Standalone React dashboard served by nginx | compose port mapping |
| 9090 | Docker `prometheus` | Prometheus UI | compose port mapping |
| 3100 | Docker `loki` | Loki log ingestion | compose port mapping |
| 3001 | Docker `grafana` | Grafana UI (default login `admin` / `admin`) | compose port mapping |
| 514/udp | outbound only | Syslog target the Admiral sends to by default (`127.0.0.1:514`) | `syslogServers` |

On a single box `rest.hostname` defaults to `localhost`, which keeps both 7890 and 7891 off the network. Setting it to
`0.0.0.0`, `*`, or `+` exposes both listeners, and the Docker configs do exactly that. When you change `admiralPort`,
also set `ARMADA_BASE_URL` before running the install, update, or health-check scripts so they probe the right
address.

## TLS

Armada's TLS story for 1.0 is to terminate TLS in front of it. The Admiral has a `rest.ssl` flag that turns on TLS in
the underlying web server, but there is no setting for a certificate path, and the MCP listener has no TLS option at
all, so the flag alone does not give you a working HTTPS endpoint on every platform. Treat it as unsupported.

The reliable setup is a reverse proxy (nginx, Caddy, Traefik, or a cloud load balancer) that holds the certificate
and forwards to `127.0.0.1:7890` and `127.0.0.1:7891`. Forward WebSocket upgrades for `/ws` and, in split mode,
`/v1.0/harbor/connect`. Keep the Admiral bound to loopback behind the proxy so nothing bypasses it.

The tunnel to Armada.Proxy is the one place where TLS is built in: give `remoteControl.tunnelUrl` a `wss://` (or
`https://`) URL and the Admiral validates the proxy's certificate. Put the proxy itself behind a TLS-terminating
front end so the browser session and the tunnel both travel encrypted. Database connections to PostgreSQL, MySQL,
and SQL Server can require encryption with `database.requireEncryption`.

## Backups and restore

Back up before every upgrade, before a restore, and on a schedule that matches how much work you can afford to lose.

For SQLite (the default provider), the Admiral has a built-in backup. `GET /api/v1/backup` returns a ZIP containing
a consistent snapshot of `armada.db` taken with the SQLite online backup API, the current `settings.json`, and a
`manifest.json` with the schema version, Armada version, and record counts. The same operation is available as the
`backup` MCP tool, and backups written by the server land in `~/.armada/backups`.

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:7890/api/v1/backup -o armada-backup.zip
```

`POST /api/v1/restore` takes that ZIP as an `application/zip` body, checks that it contains a valid `armada.db` with
a `schema_migrations` table, writes a safety backup of the current database to `~/.armada/backups` first, and then
replaces the database. Restart the server afterward; in-memory state is not reloaded until you do. Full request and
response details are in [REST_API.md](REST_API.md#backup-and-restore).

Two cautions. The backup ZIP includes `settings.json`, which can contain `gitHubToken` and database credentials, so
store backups as secrets. And the built-in backup only understands SQLite. For PostgreSQL, MySQL, or SQL Server,
use the database's own tools (`pg_dump`, `mysqldump`, or a SQL Server `BACKUP DATABASE`) and copy the settings file
separately. Restore those with the matching native tools while the Admiral is stopped.

In the Docker stack you can also stop the stack and copy `docker/armada/db` and `docker/armada/armada.json`; that is
a complete SQLite backup. The pre-upgrade backup procedure and how schema migrations run are covered in
[UPGRADING.md](UPGRADING.md).

## Retention

Armada prunes some data on its own and lets other data grow. Know which is which before a long-running install
fills a disk.

| What | Setting | Default |
|------|---------|---------|
| Completed voyages, missions, signals, events | `dataRetentionDays` (0 disables) | 30 days |
| Request history | `requestHistoryRetentionDays` (0 disables); `requestHistoryMaxBodyBytes` caps stored bodies | 30 days; 32 KB per body |
| Stopped or failed planning sessions | `planningSessionRetentionDays` (0 disables) | 0 (kept) |
| Fleet action runs | `fleetActions.runRetentionDays` (1 to 3650) | 30 days |
| Server rebuild slots | `rebuildSlotRetentionCount` | 3 |
| Captain log files | `maxLogFileSizeBytes`, `maxLogFileCount` (rotation) | 10 MB, 5 files |

Ask threads and messages, vessel health findings history, and vessel import batches are not pruned today and grow
without bound; settings for them are tracked as W3.4 in [V1_READINESS.md](../V1_READINESS.md). On a busy install,
check the size of the database and of `~/.armada/logs` monthly. Request history capture can be turned off entirely
with `requestHistoryEnabled: false`.

## Telemetry

Armada sends nothing to its authors. Telemetry here means exporting your own install's metrics and logs to
backends you run. It is off by default on a single box (`telemetry.enabled: false`) and on in the Docker configs,
which point Loki export at the bundled `loki` service.

When enabled, the Admiral exposes Prometheus metrics at `http://<host>:9464/metrics`, and can push to an OTLP
collector (`telemetry.otlpEndpoint`) and to Loki (`telemetry.lokiEndpoint`). The Docker stack provisions Grafana with
two dashboards (fleet operations and reliability). Metric names, settings, and the stack layout are documented in
[TELEMETRY.md](TELEMETRY.md).

## Logs

Everything the Admiral writes lives under the log directory, `~/.armada/logs` by default (`logDirectory`;
`/app/data/logs` in Docker, which maps to `docker/armada/logs` on the host).

| Path under the log directory | Contents |
|------------------------------|----------|
| `admiral.log` | The Admiral's own log; rotated by size |
| `captains/` | Per-captain process output, rotated per `maxLogFileSizeBytes` and `maxLogFileCount` |
| `missions/<missionId>.log` | Output for one mission |
| `final-messages/` | The final message each captain reported |
| `diffs/` | Diffs captured at landing |
| `instructions/` | Snapshots of the instruction files a mission was launched with |
| `docks/` | Dock start metadata |
| `playbooks/`, `objective-refinement-sessions/` | Per-run material for playbooks and objective refinement |

The Admiral also sends its log stream to the syslog targets in `syslogServers` (by default `127.0.0.1:514`) and, with
telemetry on, to Loki. In Docker, `docker compose logs armada-server` shows the console output. Armada.Proxy logs to
its own `logDirectory` (`docker/proxy/logs` in Docker).

## Troubleshooting

**The server will not start and the log says the address is in use.** Another process holds 7890 or 7891, often a
second Armada. Stop it, or move this one with `admiralPort` and `mcpPort`. Remember to export `ARMADA_BASE_URL` for
the scripts afterward.

**The scripts report the server never became healthy.** `scripts/common/healthcheck-server.sh` polls
`/api/v1/status/health` for about thirty seconds. Run `curl -fsS http://127.0.0.1:7890/api/v1/status/health`
yourself; if it fails, the reason is near the end of `admiral.log`. A schema migration on a large database can push
the first start past the probe window, so check the log before restarting.

**`/dashboard` returns 404 or an old dashboard.** The Admiral serves the React build from `dashboardPath` (on a
single box, `~/.armada/dashboard`). Re-run `scripts/common/deploy-dashboard.sh`, or point `dashboardPath` at
`src/Armada.Dashboard/dist`.

**Missions sit in Pending forever in split mode.** With `requireHarborForLaunch: true`, a mission waits for a
connected Harbor owned by the same user that advertises the requested runtime. Check the Harbors page; if the Harbor
shows Degraded or Disconnected, its heartbeats stopped arriving within `harbor.heartbeatTimeoutSeconds` (45 seconds by
default).

**A captain launched from a Harbor cannot reach MCP.** The Harbor hands captains the URL in
`harbor.advertisedMcpBaseUrl`. From the Harbor host, `curl` that URL. In the split compose file it is
`http://127.0.0.1:7891/mcp`, which only works when the Harbor runs on the Docker host.

**A captain is stuck Working with no output.** The heartbeat watchdog marks captains stalled after
`stallThresholdMinutes` and attempts recovery up to `maxRecoveryAttempts` times. Read `captains/` and
`missions/<missionId>.log` for the last thing the agent printed; an expired agent login or a usage limit is the most
common cause, and the captain is quarantined for `captainQuarantineMinutes` after one.

**The Docker container exits with database permission errors.** The container user must be able to write
`docker/armada/db` and `docker/armada/logs`. Fix the ownership on the host rather than loosening permissions to world
writable.

**The instance does not appear in the proxy.** Work through the failure modes in
[TUNNEL_OPERATIONS.md](TUNNEL_OPERATIONS.md#common-failure-modes): a wrong scheme in `tunnelUrl`, a password mismatch,
a TLS validation failure, or an unreachable proxy each show up differently in `admiral.log`.

**A restore "succeeded" but the dashboard still shows old data.** The server must be restarted after
`POST /api/v1/restore`. Until then it serves cached state.

**`npm` fails with `SELF_SIGNED_CERT_IN_CHAIN` during install or update.** A corporate TLS-inspecting proxy is in the
path. Pass `--insecure` to the script, as described in the README.

When none of these match, collect `admiral.log`, the relevant `missions/` and `captains/` files, the output of
`GET /api/v1/status`, and your settings file with secrets removed before filing an issue.
