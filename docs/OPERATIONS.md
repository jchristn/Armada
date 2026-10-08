# Operating Armada

Read this if you are the person who keeps an Armada install running: picking a deployment shape, knowing which ports
are open and why, protecting the data, keeping storage in check, and getting a stuck install moving again. It
assumes Armada is already installed. For first-time setup, start with [GETTING_STARTED.md](../GETTING_STARTED.md)
or [DOCKER.md](DOCKER.md); for moving between versions, read [UPGRADING.md](UPGRADING.md).

Most settings named below live in the Admiral's settings file. On a single box that is `~/.armada/settings.json`
(the data directory can be moved with the `ARMADA_DATA_DIR` environment variable). In the Docker stack it is
`docker/armada/armada.json`, mounted into the container at `/app/data/settings.json`. Changes to the settings file
take effect on the next server start.

## Deployment topologies

Armada has four shapes in practice, and any of them can serve other machines (see Remote Admiral below). They differ mainly in where the agent CLIs run, because a captain needs the
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

Be clear about what the container can do: the server image includes `git` but no agent CLIs, so a containerized
Admiral in Local mode can run missions on API-endpoint captains (the tool loop runs inside the Admiral) but not on
Claude Code, Codex, or the other CLI runtimes. For CLI captains from a container, use split mode below. A vessel
whose repository is a host checkout mounted into the container needs that path trusted for git (the files belong to
your host user, not the container's UID 1654); [DOCKER.md](DOCKER.md#vessels-from-repositories-mounted-into-the-container)
shows the `GIT_CONFIG_GLOBAL` setup. The
standalone dashboard on port 3000 proxies the API and WebSocket to the Admiral, so it needs no extra configuration. [DOCKER.md](DOCKER.md) covers volumes, building images, and factory reset;
`docker/update.sh` (or `docker/update.bat`) pulls the observability images, rebuilds the Admiral and dashboard from
the checkout, and recreates the stack without touching volumes.

### Split mode with a Harbor (experimental)

Split mode is marked **experimental** for 1.0 and is excluded from the compatibility promise. It works, but its
transport and settings may still change.

In split mode the Admiral runs detached (typically in Docker via `docker/armada/compose.split.yaml`) and a Harbor
runs on each machine where code and agent logins live. The Harbor dials out to the Admiral over a WebSocket on the
Admiral port at `/v1.0/harbor/connect`, receives host work (git, worktrees, captain launches), and streams results
back. Captains it launches call home to the MCP URL the Admiral advertises in `harbor.advertisedMcpBaseUrl`. The
split profile sets `deploymentMode: "Split"` and `requireHarborForLaunch: true`, so a mission waits until an eligible
Harbor owned by the requesting user is connected rather than running inside the container.

Two operational details matter here. A Harbor that presents an Armada credential (`x-access-key` or an
`Authorization` header) registers under that credential's tenant and user. A Harbor without one is accepted only when
the Admiral listens on a loopback hostname and the Harbor connects from loopback; set `harbor.requireAuth: true` to
require a credential in every case. The Docker split profile binds `0.0.0.0`, so its Harbors must present a
credential. And the advertised MCP URL must be reachable from the Harbor host, not from inside the container. [HARBOR.md](HARBOR.md) explains the model and
[HARBOR_PROTOCOL.md](HARBOR_PROTOCOL.md) the wire contract.

### Remote Admiral

Any of the shapes above can serve other machines: bind `rest.hostname` beyond loopback (or put a TLS proxy in front),
give each user or machine a bearer token, and point clients at it. The `armada` CLI targets it with `--server`,
`--token`, and `--profile` (profiles shared with `armada tui`). [REMOTE_SERVER.md](REMOTE_SERVER.md) is the
end-to-end guide.

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
`0.0.0.0`, `*`, or `+` exposes both listeners, and the Docker configs do exactly that. To run the Admiral for other
machines (credentials, TLS, and connecting the dashboard, TUI, CLI, MCP clients, and Harbors), follow
[REMOTE_SERVER.md](REMOTE_SERVER.md). When you change `admiralPort`,
also set `ARMADA_BASE_URL` before running the install, update, or health-check scripts so they probe the right
address.

## TLS

Armada's TLS story for 1.0 is to terminate TLS in front of it. The Admiral has a `rest.ssl` flag that turns on TLS in
the underlying web server, but there is no setting for a certificate path, and the MCP listener has no TLS option at
all, so the flag alone does not give you a working HTTPS endpoint on every platform. Treat it as unsupported.

The reliable setup is a reverse proxy (nginx, Caddy, Traefik, or a cloud load balancer) on the same host that holds
the certificate and forwards to `localhost:7890` and `localhost:7891`. Forward WebSocket upgrades for `/ws` and, in
split mode, `/v1.0/harbor/connect`. Keep the Admiral bound to loopback behind the proxy so nothing bypasses it. Three
details matter, each verified through Caddy: the MCP listener answers only the `Host` it is bound to, so the proxy
must send `Host: localhost:7891` upstream (otherwise MCP returns 404); with `rest.hostname: localhost` the MCP
listener may bind only `[::1]` (it did on macOS), so point the upstream at `localhost` rather than `127.0.0.1`; and
because the proxy connects from loopback, set `mcp.allowUnauthenticatedLoopback: false` and `harbor.requireAuth: true`,
or every credential-less MCP call through the proxy runs as the default tenant's tenant admin. A tested Caddyfile and
the client setup are in [REMOTE_SERVER.md](REMOTE_SERVER.md#3-tls-with-a-reverse-proxy).

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
| Inactive Ask threads (pinned threads are kept) | `retention.askThreadArchiveAfterDays`, `retention.askThreadDeleteAfterDays` (0 disables each) | archive after 90 days; never delete |
| Finished background jobs (the newest of each kind is kept) | `retention.jobRetentionDays` (0 disables) | 30 days |
| Harbor metrics: ended Harbor job records, per-minute link samples, and link events (each Harbor's latest event is kept) | `retention.jobRetentionDays` (0 disables) | 30 days |
| Finished vessel import batches | `retention.importBatchRetentionDays` (0 disables) | 90 days |
| Decided, expired, or cancelled CLI permission requests (pending ones are kept; a deleted Ask thread takes its requests with it) | `retention.cliPermissionRequestRetentionDays` (0 disables) | 90 days |
| Pre-migration database backups | `database.migrationBackupRetentionCount` | 5 |

Vessel health findings history is not pruned today and grows with the evaluation schedule. Completed voyages,
missions, signals, and events expire on SQLite only (`dataRetentionDays`); on PostgreSQL, MySQL, and SQL Server prune
them with your own database jobs. On a busy install, check the size of the database and of `~/.armada/logs` monthly.
Request history capture can be turned off entirely
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
| `playbooks/`, `planning-sessions/`, `objective-refinement-sessions/` | Per-run material for playbooks, planning sessions, and objective refinement |

The Admiral also sends its log stream to the syslog targets in `syslogServers` (by default `127.0.0.1:514`) and, with
telemetry on, to Loki. In Docker, `docker compose logs armada-server` shows the console output. Armada.Proxy logs to
its own `logDirectory` (`docker/proxy/logs` in Docker).

## Reading captain papercuts

Captains report friction they meet on an `[ARMADA:PAPERCUT]` line: a stale
document, a dead link, a brief that contradicts itself, a missing sibling
repository, a test that fails under load. Armada stores each report as a
`papercut` event with the reporting mission, captain, vessel, and voyage, and
the `papercut_summary` MCP tool reads them back collapsed into groups of
the same vessel, category, and problem.

Read them on a schedule. A report that nobody reads is worse than no report:
the captain paid to write it and the next captain still pays the same cost.

1. Run `papercut_summary` after a voyage closes, and again in the weekly
   sweep with `sinceHours: 168`.
2. Read the count and the distinct-captain count first. One captain reporting
   a problem is an anecdote. Several captains reporting it is a defect with
   evidence.
3. Route the group by category:

   | Category | Owner |
   | --- | --- |
   | `MissingDoc`, `BrokenLink`, `RepoFriction`, `TestFlake`, `Other` | Backlog item on that vessel |
   | `EnvSetup` | Dock or workflow-profile fix, then a Check to prove it |
   | `BriefContradiction`, `PlatformBug` | Armada objective, direct-edit only |
   | `ToolFailure` | Read the mission log before you accept it; a captain calling a tool it never received is a `BriefContradiction` |

4. Quote the group in the record you create: the count, the distinct-captain
   count, the sample title, and the sample mission IDs. Those missions are the
   evidence.
5. Keep the promotion manual. A high count is not authority to dispatch.

Two signals need a different response than a repository fix:

- **A `BriefContradiction` group is a captain-quality defect, not a vessel
  defect.** It means the brief asks for something the captain cannot do. Fix
  the instruction module, not the repository.
- **A category that one runtime reports and no other runtime reports** is
  usually about that runtime, not about the vessel. Compare the reports before
  you change vessel code.

Judge missions do not file papercuts. A judge reports what it finds through
its verdict, and splitting review feedback across two surfaces means the
operator reads only one of them.

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
connected Harbor owned by the same user that advertises the requested runtime. A Harbor's owner comes only from the
credential it presents (`AccessKey` in the Harbor app): a credential-less loopback Harbor has no owner and never counts,
so give the Harbor one of the mission user's credentials. Check the Harbors page; a Harbor shows Disconnected once its
link closes (the Admiral does not time out heartbeats). Missions that were already running on a Harbor when it
disconnected are not moved to another Harbor: the stall watchdog (`stallThresholdMinutes`) relaunches them on that Harbor
once it is back; while it is still offline and the policy is on, each recovery attempt waits another stall interval, and
the mission fails with `StallRecoveryExhausted` when `maxRecoveryAttempts` run out.

**A captain launched from a Harbor cannot reach MCP.** The Harbor hands captains the URL in
`harbor.advertisedMcpBaseUrl`. From the Harbor host, `curl` that URL. In the split compose file it is
`http://127.0.0.1:7891/mcp`, which only works when the Harbor runs on the Docker host.

**A captain is stuck Working with no output.** The heartbeat watchdog marks captains stalled after
`stallThresholdMinutes` and attempts recovery up to `maxRecoveryAttempts` times. Read `captains/` and
`missions/<missionId>.log` for the last thing the agent printed; an expired agent login or a usage limit is the most
common cause, and the captain is quarantined for `captainQuarantineMinutes` after one.

**The Docker container exits with database permission errors.** The container user (UID 1654) must be able to
write `docker/armada/db` and `docker/armada/logs`. Fix the ownership on the host (`sudo chown -R 1654:1654
docker/armada/db docker/armada/logs`) rather than loosening permissions to world writable.

**Docker missions stay Pending and `admiral.log` shows `detected dubious ownership`.** The vessel's repository is a
path mounted from the host, so it is owned by another user than the container's, and git refuses it. Trust the path
as described in [DOCKER.md](DOCKER.md#vessels-from-repositories-mounted-into-the-container). Setting
`safe.directory` through `GIT_CONFIG_COUNT` / `GIT_CONFIG_KEY_0` or `git -c` does not work for this case.

**A captain says a shell command was refused, or a mission waits on approvals.** That is the CLI tool permission
policy. The first `Armada:` line of `missions/<missionId>.log`, and the CLI tools note in an Ask conversation header,
say which policy the launch used, where it came from, and where to change it (`ApproveInArmada` runs as `Refuse` on
runtimes other than Claude Code and ApiEndpoint, on Harbors, and for Claude Code without a scoped MCP token). Pending requests are listed at
`/cli-permissions` in the dashboard and in the TUI Approvals center; an undecided request is denied after
`permissions.promptTimeoutSeconds` (default 600, 10 to 3600). The server defaults are `permissions.askDefaultPolicy`
(`ApproveInArmada`) and `permissions.missionDefaultPolicy` (`Bypass`), and `permissions.allowOwnerApproval` (default
false) lets owners decide their own requests; change them in `settings.json` or Settings > CLI Tool Permissions (they
apply to the next launch). See [CAPTAINS.md](CAPTAINS.md#cli-tool-permissions).

**The instance does not appear in the proxy.** Work through the failure modes in
[TUNNEL_OPERATIONS.md](TUNNEL_OPERATIONS.md#common-failure-modes): a wrong scheme in `tunnelUrl`, a password mismatch,
a TLS validation failure, or an unreachable proxy each show up differently in `admiral.log`.

**A restore "succeeded" but the dashboard still shows old data.** The server must be restarted after
`POST /api/v1/restore`. Until then it serves cached state.

**`npm` fails with `SELF_SIGNED_CERT_IN_CHAIN` during install or update.** A corporate TLS-inspecting proxy is in the
path. Pass `--insecure` to the script, as described in the README.

When none of these match, collect `admiral.log`, the relevant `missions/` and `captains/` files, the output of
`GET /api/v1/status`, and your settings file with secrets removed before filing an issue.

## Service and startup registration

The Admiral and Harbor register themselves with the operating system. The installers call the same flags (the WiX
and Inno installers on Windows, the `.pkg` on macOS, and the Deb/Rpm package scripts on Linux), so a manual install and
a packaged one end up with the same definition, and you can repair or remove either by hand. The scripts in
[RUN_ON_STARTUP.md](RUN_ON_STARTUP.md) predate these flags and still work; do not use both for the same install, or
two Admirals will race for the same ports. On macOS, `--install-service` warns when it finds the scripts' agent
(`com.armada.admiral`).

### Admiral: `--install-service`, `--uninstall-service`, `--run-service`

```bash
armada-server --install-service            # register, enable, and start
armada-server --install-service --dry-run  # print the definition and the commands, change nothing
armada-server --install-service --no-start # register and enable, start later
armada-server --uninstall-service          # stop and remove
```

`--run-service` is what the service manager passes when it starts the Admiral. It runs the same server as a plain
launch, without the banner and without console colors, and on Windows it runs under the service control manager.
You do not normally type it.

What `--install-service` creates depends on the platform and on whether you run it elevated:

| Platform | As a normal user | Elevated (root, or an administrator prompt) |
|----------|------------------|---------------------------------------------|
| Linux | `~/.config/systemd/user/armada.service` (`systemd --user`, `WantedBy=default.target`), then `systemctl --user enable` and start | `/etc/systemd/system/armada.service` (`WantedBy=multi-user.target`), then `systemctl enable` and start. Add `--service-user <account>` to set `User=`; without it the service runs as root |
| macOS | `~/Library/LaunchAgents/com.joelchristner.armada.server.plist`, bootstrapped into your GUI session | `/Library/LaunchAgents/com.joelchristner.armada.server.plist` for every user, bootstrapped into the session of the user at the console (this is what the `.pkg` does) |
| Windows | Refused with exit code 4: creating a service needs an elevated prompt | Windows Service `armada` ("Armada Admiral"), automatic start, restart on failure (5 s, 5 s, 60 s), then started |

Where the Admiral keeps its data follows the account it runs as, which matters more than it looks. A captain needs the
code, git credentials, and the agent CLI logins of a real user. A user-scope systemd unit or a LaunchAgent runs as
you, with `~/.armada` as usual. A Linux system unit without `--service-user` runs as root and uses `/root/.armada`.
The Windows service runs as LocalSystem by default; with no `ARMADA_DATA_DIR` set it keeps its data in
`%ProgramData%\Armada`, and captains launched from it do not see your logins. To run it as yourself, set the account
after installing with `sc.exe config armada obj= .\<you> password= <password>` and restart it; the data then lives in
that account's `.armada` directory.

Both flags are idempotent. Installing twice leaves one registration: an unchanged definition is left alone (and only
started if it is not running), a changed one is rewritten and the service restarted (systemd) or reloaded (launchd);
on Windows the existing service is reconfigured with `sc.exe config`. Uninstalling something that is not installed
prints "nothing to do" and succeeds. Every step is printed, and the exit code tells an installer what happened:

| Exit code | Meaning |
|-----------|---------|
| 0 | Done, nothing to do, or dry run |
| 1 | A file could not be written or a service-manager command failed (the error is printed) |
| 2 | Invalid arguments (two actions, a bad `--service-user`, or a Harbor flag passed to the Admiral) |
| 3 | No implementation for this operating system |
| 4 | Wrong privileges (a Windows service without elevation, or Harbor's login item as root) |

Check the result with `systemctl [--user] status armada.service`, `launchctl print gui/$(id -u)/com.joelchristner.armada.server`,
or `sc.exe query armada`. The service writes `admiral.log` in its data directory like any other launch.

The packages wire these flags in as follows. The WiX `.msi` runs `--install-service` as a deferred custom action
after the files are installed and fails the install when it returns non-zero; uninstall runs `--uninstall-service`.
The Deb and Rpm packages run `--install-service` from their after-install script (as root, so a system unit) and
`try-restart` the service on upgrade; removal runs `--uninstall-service`. Both scripts skip registration when systemd
is not running, for example inside a container, and never fail the package transaction. The `.pkg` postinstall runs
`--install-service` and `/usr/local/lib/armada-server/uninstall.sh` runs `--uninstall-service`.

### Harbor: `--install-startup`, `--uninstall-startup`

```bash
armada-harbor --install-startup            # start Harbor in the tray at every login
armada-harbor --install-startup --dry-run  # print the entry, change nothing
armada-harbor --uninstall-startup
```

Harbor's login item belongs to the user who runs Harbor, so the flags refuse to run as root on Linux and macOS. The
item starts Harbor with `--minimized`: it connects to the Admiral from the tray without opening its window. Nothing is
launched when you register; the item takes effect at the next login. Removing it does not stop a Harbor that is
already running.

| Platform | Login item |
|----------|------------|
| Windows | Value `Armada Harbor` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The Inno installer runs `--install-startup` as the user who started Setup |
| macOS | `~/Library/LaunchAgents/com.joelchristner.armada.harbor.plist` with `RunAtLoad` and no `KeepAlive`, so quitting from the menu bar keeps it closed. It runs the binary inside the bundle with `--minimized`, so Harbor starts as a menu bar icon only (the bundle sets `LSUIElement`). The `.dmg` has no installer step; run `"/Applications/Armada Harbor.app/Contents/MacOS/Armada.Harbor" --install-startup` once after copying the app to Applications (the item records the path it was run from) |
| Linux | `~/.config/autostart/armada-harbor.desktop` (honors `XDG_CONFIG_HOME`). The Deb/Rpm package does not register it for you; run `armada-harbor --install-startup` as yourself |

The Windows paths of both programs (the service host, `sc.exe`, and `reg.exe`) are covered by tests of the exact
command lines they run, not by a run on Windows. Treat the first Windows install of a release as the real check.
