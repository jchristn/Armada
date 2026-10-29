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

### Admiral rollouts while missions run

An Admiral restart leaves Harbor-hosted mission agents running. Before accepting Harbor links, the new Admiral
restores ownership from the persisted mission, captain, dock and Harbor job records. The reconnect acknowledgement
retains those jobs, and their output and exit events continue through the mission lifecycle. Mission ID, process ID,
branch, dock, start time and `RedispatchAttempts` remain unchanged when no new agent was launched. The mission log
records an explicit reattachment note rather than another “Agent starting” entry. Explicit captain stop and recall
operations still stop remote agents.

Startup allows restored jobs `harbor.heartbeatTimeoutSeconds` to reconnect before normal stall recovery applies.
If the Harbor reports that a job disappeared, Armada waits for its replayed exit, then handles a missing job as an
interruption. A real recovery launch retains the existing worktree and its uncommitted changes, adds continuation
instructions, and increments `RedispatchAttempts` before attempting the launch. Recovery limits still apply;
exhausted infrastructure interruptions are reported as infrastructure failures rather than model crashes.
Admiral-local agents cannot outlive their host restart and use this counted recovery path. The Admiral's database
and log directory must be persistent across image updates. This does not provide a distributed lease for multiple
Admirals running against the same database simultaneously, or preserve in-memory model execution across a Harbor
process restart.

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
| Black box entries (pinned entries are kept; see [Black box retention and storage](#black-box-retention-and-storage)) | `blackBox.retention.*` | 180 days; 30 days after resolution; 1000 per vessel; 1 GB in total |

Vessel health findings history is not pruned today and grows with the evaluation schedule. Completed voyages,
missions, signals, and events expire on SQLite only (`dataRetentionDays`); on PostgreSQL, MySQL, and SQL Server prune
them with your own database jobs. On a busy install, check the size of the database and of `~/.armada/logs` monthly.
Request history capture can be turned off entirely
with `requestHistoryEnabled: false`.

## Black box retention and storage

The black box keeps one entry per failed mission or voyage attempt: an index row in `blackbox_entries` plus a few
objects (`record.json`, the briefs, bounded evidence). The index rows are always in the Armada database; the object
bytes go to the configured storage target, `Database` by default. Black box data has its own retention and garbage
collector; `dataRetentionDays` and `retention.*` never touch it. The routes below that change or report on the whole
install need a global admin, except `GET /api/v1/blackbox/retention` (tenant admins see their own tenant) and the
per-entry routes (anyone who can see the entry). Every setting is in
[UPGRADING.md](UPGRADING.md#the-mission-and-voyage-black-box-migrations-103-and-104).

### Checking retention

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:7890/api/v1/blackbox/retention
```

```json
{"Policy":{"RetentionDays":180,"ResolvedRetentionDays":30,"KeepLatestPerMission":true,"MaxEntriesPerVessel":1000,"MaxTotalBytes":1073741824,"DeleteWithMission":false,"DeleteWithVessel":true,"GcIntervalMinutes":60,"GcBatchSize":200,"OrphanGraceHours":24},"TotalEntries":3,"TotalBytes":28653,"PinnedEntries":0,"DeletingEntries":0,"ExpiredEntries":0,"NextRunUtc":"2026-10-29T22:10:34.416490Z"}
```

After the first garbage collection run the response also carries `LastRun`, that run's report. MCP: `get_blackbox_retention`.

How an entry's expiry is set: `retentionDays` after capture (or the vessel's `BlackBoxRetentionDays` when set; 0 keeps
forever), or `resolvedRetentionDays` after it was resolved when that is sooner. Pinned entries never expire. Expiry is
recomputed when an entry is resolved, pinned or unpinned, when the retention settings change, and for one vessel's
entries when its `BlackBoxRetentionDays` changes. With `keepLatestPerMission` (default), the current entry of a
mission or voyage that still exists is kept past its expiry.

### Garbage collection

The collector runs every `gcIntervalMinutes` (default 60) while `blackBox.enabled` is true. Each run, in order:
finishes deletes left in the Deleting state, deletes expired entries, enforces the quotas, deletes entries whose vessel
was deleted (`deleteWithVessel`, default true) or whose mission row is gone (`deleteWithMission`, default false), sweeps
orphaned objects from external stores, and marks entries whose objects have vanished from their store as
ObjectMissing. It then emits `blackbox.gc_completed` with the counts.

Run it on demand, dry run first. A dry run counts and samples what would go and deletes nothing:

```bash
curl -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  http://127.0.0.1:7890/api/v1/blackbox/gc -d '{"DryRun": true}'
```

```json
{"DryRun":true,"StartedUtc":"2026-10-29T21:11:39.927512Z","FinishedUtc":"2026-10-29T21:11:39.933498Z","DurationMs":5,"Expired":0,"Evicted":0,"Cascaded":0,"RetriedDeletes":0,"OrphansRemoved":0,"ObjectsMissing":0,"BytesFreed":0,"Failures":0,"CandidateEntryIds":[],"Errors":[]}
```

`CandidateEntryIds` lists at most 100 ids. Send `{"DryRun": false}` (the default for this route) to run it for real.
MCP: `run_blackbox_gc {"dryRun": true}`.

**Quotas and what goes first.** When a vessel holds more than `maxEntriesPerVessel` (default 1000) entries, its oldest
unpinned entries that are not the current entry of a mission are evicted down to the limit. Then, while the stored
bytes of all entries exceed `maxTotalBytes` (default 1 GB), the oldest unpinned resolved entries go first, then the
oldest unpinned entries of any resolution; with `keepLatestPerMission`, the current entry of a live mission or voyage
stays. Pinned entries are never evicted. Deleting a tenant deletes all of its entries, pinned ones included.

### Purging by filter

`POST /api/v1/blackbox/purge` deletes every entry that matches the list filters (`VesselId`, `MissionId`, `Phase`,
`Step`, `Triage`, `Resolution`, `CreatedBefore` and the rest of the `GET /api/v1/blackbox` filters), oldest first, up to
`MaxEntries` (1 to 100000, default 10000). `DryRun` defaults to true, and pinned entries are skipped unless
`IncludePinned` is true.

```bash
curl -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  http://127.0.0.1:7890/api/v1/blackbox/purge \
  -d '{"VesselId": "vsl_mvu1ffz5_4Vb0gY5fhKv", "CreatedBefore": "2026-09-01T00:00:00Z"}'
```

```json
{"DryRun":true,"Matched":0,"Deleted":0,"Pending":0,"SkippedPinned":0,"BytesFreed":0,"SampleEntryIds":[]}
```

Read `Matched` and `SampleEntryIds` (at most 100), then repeat with `"DryRun": false`. `Pending` counts entries whose
store delete failed; they stay Deleting and the collector finishes them. MCP: `purge_blackbox`. To delete specific
entries, use `DELETE /api/v1/blackbox/{id}` or `POST /api/v1/blackbox/delete {"Ids": [...]}` (at most 1000 ids; tenant
admins), or MCP `delete_blackbox_entries`.

### Pinning

Pin an entry you want to keep (an incident you are still investigating, a regression fixture you have not exported):
`POST /api/v1/blackbox/{id}/pin`, MCP `pin_blackbox {"entryId": "bbx_mvu1ftcv_zcdofOBmjhY"}`. A pinned entry has no
expiry and is skipped by quotas, cascades and purges (unless `IncludePinned`). `POST /api/v1/blackbox/{id}/unpin` (or
`pin_blackbox` with `"pinned": false`) recomputes its expiry from the policy.

### Storage status and health

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:7890/api/v1/blackbox/storage
```

```json
{"Target":"Database","Health":{"Target":"Database","Healthy":true,"Message":"Database store reachable.","LatencyMs":0,"CheckedUtc":"2026-10-29T21:11:39.911573Z"},"FallbackToDatabase":true,"Usage":[{"Target":"Database","Entries":3,"Bytes":28653}]}
```

`Usage` has one row per target that holds entries (entries written before a target change stay where they were
written). MCP: `get_blackbox_storage`. The doctor check "Black Box Storage" in `GET /api/v1/doctor` runs the same
health check: Pass when the configured target validates and answers, Fail with the misconfigured setting or the store's
error plus what fallback will do, Warn when `blackBox.enabled` is false. The host storage report also lists one
`BlackBox` location per target that holds entries.

Choosing a target: keep `Database` unless the database should not hold the bytes. `Disk` writes under
`{DataDirectory}/blackbox` (or `blackBox.storage.disk.directory`) on the Admiral host; `S3` (AWS or an S3-compatible
store such as MinIO), `AzureBlob`, `GoogleCloud`, `Cifs` and `Nfs` take their settings under `blackBox.storage`. Objects
are stored at `{keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}/{objectName}`. `PUT /api/v1/settings`
refuses invalid storage settings (400 naming the setting) before it changes anything, and the new target applies to new
entries only.

**Fallback.** When a write to an external store fails and `fallbackToDatabase` is true (the default), the entry is
written to the database instead and a `blackbox.storage_fallback` event (`entryId`, `from`, `to`, `error`) is emitted;
watch for it after changing targets or when the store has an outage. With fallback off, `record.json` still goes to the
database (so the capture is never lost), the evidence objects are marked missing, and the entry is ObjectMissing.

### Migrating to another storage target

Configure the new target in `blackBox.storage` first (the migration validates its settings and runs its health check
before it starts), then start a job:

```bash
curl -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  http://127.0.0.1:7890/api/v1/blackbox/storage/migrate -d '{"To": "Disk", "DeleteSource": false, "BatchSize": 100}'
```

```json
{"Id":"bbm_mvu1gk3j_HlaYvqBefrk","State":"Pending","To":"Disk","DeleteSource":false,"BatchSize":100,"TotalEntries":3,"MigratedEntries":0,"FailedEntries":0,"BytesCopied":0,"CreatedUtc":"2026-10-29T21:11:39.967161Z","LastUpdateUtc":"2026-10-29T21:11:39.967774Z"}
```

Poll it until `State` is `Completed` or `Failed`:

```bash
curl -H "Authorization: Bearer <token>" http://127.0.0.1:7890/api/v1/blackbox/storage/migrations/bbm_mvu1gk3j_HlaYvqBefrk
```

```json
{"Id":"bbm_mvu1gk3j_HlaYvqBefrk","State":"Completed","To":"Disk","DeleteSource":false,"BatchSize":100,"TotalEntries":3,"MigratedEntries":3,"FailedEntries":0,"BytesCopied":28653,"LastEntryId":"bbx_mvu1ftew_95PyT7WmtrV","CreatedUtc":"2026-10-29T21:11:39.967161Z","StartedUtc":"2026-10-29T21:11:39.970439Z","FinishedUtc":"2026-10-29T21:11:39.982809Z","LastUpdateUtc":"2026-10-29T21:11:39.982810Z"}
```

- `To` is required; `From` (optional) limits the job to entries on one target. `BatchSize` defaults to 50 and is
  clamped to 1 to 1000. `DeleteSource` **defaults to true**: send `false` to keep the source copies until you have
  checked the new target.
- Per entry, every object is read and checked against its SHA-256, written to the target, read back and checked again;
  only then is the entry switched to the new target and (with `DeleteSource`) the source copy deleted. A corrupt or
  unreadable object fails only its entry, which stays where it was and readable; the job ends `Failed` when any entry
  failed (`FailedEntries`, `LastError`), so fix the cause and start the same migration again.
- The job saves its cursor after every entry and resumes on the next Admiral start. Running it again skips entries
  already moved. Only one migration runs at a time; starting the same one again returns the running job.
- Migrating does not change `blackBox.storage.target`. Set it to the new target so new entries go there too.

### Backups

The SQLite backup (`GET /api/v1/backup`, MCP `backup`) contains the Database-target entries inside `armada.db`. Entries
on an external target are not copied: their index rows are in the backup, and `manifest.json` gains a `blackBox`
section (`databaseEntries`, `databaseBytes`, `externalEntries`, `externalTargets`, and `externalEntryRefs` with entry
id, target, storage prefix, object count and bytes for at most 10000 entries, plus `externalEntryRefsTruncated`). Back
up the external store with its own tools. A restore reports the same counts (and up to 100 external entry ids) in its
`BlackBox` section and says in its message when restored entries point at objects outside the database. On
PostgreSQL, MySQL and SQL Server, your database dump holds the Database-target entries.

### Metrics

With telemetry on, the Admiral exports (Prometheus names, see [TELEMETRY.md](TELEMETRY.md)):

| Metric | Meaning |
|---|---|
| `armada_blackbox_entries_total{phase,step,kind,triage}` | Entries captured |
| `armada_blackbox_capture_failures_total` | Captures that failed |
| `armada_blackbox_bytes{target}` | Stored bytes per target, refreshed after each garbage collection run and each storage status read |
| `armada_blackbox_gc_deleted_total{reason}` | Entries and orphaned objects deleted, by reason (`expired`, `evicted`, `cascaded`, `retried`, `orphan`) |

A rise in `armada_blackbox_capture_failures_total` means failures are going unrecorded inline; the sweeper captures
them later at lower fidelity.

### Troubleshooting the black box

**503 `BlackBoxStoreUnavailable`.** The entry's store could not be reached (or the black box is not running on this
server). Read `GET /api/v1/blackbox/storage` and the "Black Box Storage" doctor check for the store's error; fix the
endpoint, credentials or mount. Capture keeps working meanwhile when `fallbackToDatabase` is on.

**410 `BlackBoxObjectMissing`.** The object's row exists but its bytes are gone from the store (deleted outside Armada,
or a bucket or directory was replaced). The collector checks a rotating batch of `gcBatchSize` entries per run and marks such an
entry ObjectMissing; the index row (phase, step, reason excerpt, summary, triage) still describes the failure. Restore the store from its backup to get the bytes back. A 409
`BlackBoxObjectCorrupt` means the bytes are there but fail their SHA-256 check.

**An entry stays Deleting.** A delete removes the external bytes before the rows; when the store refuses, the entry
stays Deleting (hidden from lists, 409 `BlackBoxDeleting` on reads) and `GET /api/v1/blackbox/retention` counts it in
`DeletingEntries`. Fix the store, then let the next run finish it or run `POST /api/v1/blackbox/gc {"DryRun": false}`
(a dry run does not retry deletes).

**An entry stays EnrichmentFailed.** Enrichment is retried by the sweeper with exponential backoff, up to
`blackBox.sweeper.maxEnrichmentAttempts` (default 5). The panel and `GET /api/v1/blackbox/{id}` show `LastError`. A
tenant admin can run it again with `POST /api/v1/blackbox/{id}/reanalyze` (MCP `reanalyze_blackbox`), and a backfill
re-queues every entry whose enrichment did not finish. A failing enricher (an unreachable Harbor, a git command that
fails) does not cause this state; it leaves a note in the record's `truncation.notes` instead.

**An entry says it was captured by the sweeper or the backfill.** `CaptureMode` `Sweeper` means the failure site did
not capture inline (the capture failed or timed out, or the path was not wired) and the sweeper found the failed
mission later; `Backfill` means it came from `POST /api/v1/blackbox/backfill`. Both are built from the stored mission,
dock and event rows, so the exception, the command output and the checkout state at the moment of failure are usually
missing and the analysis has lower confidence. Git facts that still exist (the worktree, the branch, the target) are
read during enrichment.

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

## When a mission fails: the black box

Start with the black box. When a mission or voyage fails, the Admiral writes an entry at the moment of failure (where:
phase, step, host and code site; how: exception, exit code, failing command or git error; why: rule findings with
confidence; a triage verdict saying whose fix it most likely is; and ranked recovery paths). Read it before you open
logs: it quotes the evidence and names the next step.

- **Dashboard:** the mission (or voyage) page has a Black box panel. **Copy FIX THIS** copies the brief for the selected
  audience, ready to paste into a coding agent; **Ask Armada about this failure** opens Ask Armada with a prompt about
  it; the panel also downloads the brief (.md) and every object (.zip), and resolves, pins and reanalyzes the entry.
- **REST:** `GET /api/v1/missions/{id}/blackbox` (the entry and its record) and
  `GET /api/v1/missions/{id}/blackbox/brief` (the FIX THIS brief as Markdown); the voyage routes are the same under
  `/api/v1/voyages/{id}`. `?audience=agent` (default) is the brief for a captain fixing the work, `user` is the plain
  account for an operator, `maintainer` is the Armada-defect view; `redactHosts=true` strips absolute paths, host and
  user names before you share it.
- **MCP:** `get_blackbox {"missionId": "msn_mvu1fjp0_9Wd6Uuyp262"}` returns a summary of about 2 KB; add
  `"format": "brief"` for the brief or `"format": "json"` for the whole record, then `read_blackbox_section` and
  `read_blackbox_object` for detail.

```bash
curl -H "Authorization: Bearer <token>" \
  "http://127.0.0.1:7890/api/v1/missions/msn_mvu1fjp0_9Wd6Uuyp262/blackbox/brief?audience=user"
```

```markdown
# What went wrong: Retry landings deferred on a dirty checkout (mission msn_mvu1fjp0_9Wd6Uuyp262)

This is what Armada recorded when the mission failed, and what you can do next.

## Summary

Mission "Retry landings deferred on a dirty checkout" (msn_mvu1fjp0_9Wd6Uuyp262) failed during Execution at step
ProcessCrash (kind Crash). ... Triage: Transient (low confidence). Suggested next step: restart the mission
(bbx-exec-006.restart).

## What you can do

### 1. Restart the mission

Risk: Low. Expected: A fresh process runs the mission.
...
```

Capture writes the entry at once; enrichment (git state, conflicting missions, evidence) and analysis follow in the
background, so a brand-new entry can show "Analysis is still running" for a few seconds. The panel fills in when the
`blackbox.analyzed` event arrives. A 404 `BlackBoxNotFound` means no visible entry exists: the failure predates the
upgrade and has not been swept yet (see the backfill in [UPGRADING.md](UPGRADING.md#the-mission-and-voyage-black-box-migrations-103-and-104)),
the entry expired, or it belongs to another tenant.

**Recovering: dry run, then confirm.** Run only a recovery path recorded in the entry, never a free-form command. The
dry run returns the plan and a confirm token (single use, valid 10 minutes, bound to the entry, the path, you, and the
mission's state):

```bash
curl -X POST -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  http://127.0.0.1:7890/api/v1/blackbox/bbx_mvu1ftcv_zcdofOBmjhY/recovery/bbx-exec-006.restart -d '{"DryRun": true}'
```

```json
{"EntryId":"bbx_mvu1ftcv_zcdofOBmjhY","PathId":"bbx-exec-006.restart","DryRun":true,"ConfirmToken":"bbc_db902eef4c9155c21cce09b96116f9e4","ConfirmTokenExpiresUtc":"2026-10-29T21:21:39.754998Z","Plan":[{"Order":1,"Description":"Restart msn_mvu1fjp0_9Wd6Uuyp262.","McpCommand":"restart_mission {\"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\"}","RestCommand":"POST /api/v1/missions/msn_mvu1fjp0_9Wd6Uuyp262/restart","CliCommand":"armada mission restart msn_mvu1fjp0_9Wd6Uuyp262","Action":{"Tool":"restart_mission","Arguments":{"missionId":"msn_mvu1fjp0_9Wd6Uuyp262"}},"DataLossRisk":false}],"StepResults":[],"Succeeded":true,"Refused":false,"UnmetPreconditions":[],"Message":"Would run: restart_mission {\"missionId\": \"msn_mvu1fjp0_9Wd6Uuyp262\"}. Confirm with confirmToken before 2026-10-29 21:21:39Z."}
```

Read the plan, then run it with `{"DryRun": false, "ConfirmToken": "bbc_db902eef4c9155c21cce09b96116f9e4"}`. A path
with `DataLossRisk` (deleting a branch, forcing a dock reclaim) also needs `"AcknowledgeDataLoss": true`. Each step is
authorized as if you had called its tool yourself (403 when you may not). A missing, used or expired token, or a
mission or plan that changed since the dry run, is 409 `BlackBoxConfirmationRequired`: dry run again. Every dry run and
run is noted on the entry and emits `blackbox.recovery_run`. Configuration recommendations (for example a vessel's
landing mode) apply the same way through `POST /api/v1/blackbox/{id}/recommendations/{recommendationId}/apply`. MCP:
`run_blackbox_recovery` and `apply_blackbox_recommendation`, with the same dry run and token.

**Closing the loop.** Record the outcome so the next reader does not repeat the work:

- Note what you found: `POST /api/v1/blackbox/{id}/notes {"Body": "..."}` (MCP `annotate_blackbox`).
- Resolve: `POST /api/v1/blackbox/{id}/resolve {"Resolution": "Resolved", "Note": "Restarted after the test host fix", "Link": "<commit or PR>"}`
  (`Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`; MCP `resolve_blackbox`). A resolved entry expires
  `blackBox.retention.resolvedRetentionDays` (default 30) after resolution.
- Pin an entry you need to keep past retention: `POST /api/v1/blackbox/{id}/pin` (MCP `pin_blackbox`).

Repeated failures of the same kind share a fingerprint: `POST /api/v1/blackbox/fingerprints/enumerate` (MCP
`list_blackbox_fingerprints`) lists failure modes by count, and `POST /api/v1/blackbox/hotspots/enumerate` (MCP
`list_blackbox_hotspots`) the directories that keep conflicting. Storage, retention and garbage collection are in
[Black box retention and storage](#black-box-retention-and-storage); the full guide is [BLACKBOX.md](BLACKBOX.md).

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

**Final-stage sequencing.** An Architect's `waitForOtherMissions` barrier waits for sibling implementation pipelines, including their reviews and checks, rather than starting when Workers merely produce a commit. It excludes its own downstream review and the Architect to avoid circular waits. Reworks retain the barrier, including existing reworks created before inheritance was persisted.

**Architect slices and rework.** Each implementation slice has its own bounded Judge rework budget. A slice starts a new budget even when it depends on an earlier slice that passed after escalation; unchanged-work detection also stays within the current slice.

**Restarting a rejected Judge.** Prior structured rejection findings are included as historical context in the restarted review. The Judge must independently recheck them against the current requirements and code and distinguish resolved, superseded and reproducible findings; the previous verdict is not automatically reused.

**A Judge rejects the last rework.** Mission details and the inbox retain why automatic rework stopped: the bounded rework/escalation limit, a missing Worker dependency, or no eligible alternate Worker captain. For an eligibility failure, check captain availability, ownership/scope, tier and persona priorities. Read the Judge's affected-case report and dispatch a follow-up from the preserved work branch with an independent Judge; rejected work has not been approved for landing.

**An inference mission fails because of configuration.** Open the dashboard inbox entry or mission details for the recorded provider error and remediation. Mission details link to the captain, its inference endpoints, and the vessel configuration. Authentication failures point to credentials; model-not-found failures point to the URL, model ID, and account access. Rejected requests (HTTP 400, 405, 415, or 422) require checking provider/model options and tool support, but may also indicate an SDK integration defect. Correct and validate the affected configuration before restarting. Endpoint health alone does not prove that its model supports the mission tool workflow.

**A reviewed provider upgrade passes tests but still fails in use.** Client-construction tests and a green build do not verify provider URLs, authentication, payloads, health probes, or configuration create/edit/save transitions. Exercise those paths through the actual client against local HTTP fixtures, and distinguish them from live-provider validation. Worker guidance now requests this evidence; Judge re-review must check cumulative mission changes against the original requirements, rather than only the last correction. Required role contracts are delivered to the generated mission file even when a captain has no custom system instructions; older builds constructed the contract but omitted that section for default captains. These instructions improve coverage but do not prove that every case was tested.

**Images do not include a reviewed rework.** A mission that runs a publishing `build-all` script can overwrite image tags before its Judge runs, and a later source rework does not automatically republish those tags. Verify publication separately from source landing. Run the build/publish command through a Command fleet action against the reviewed default branch after landing; a source-review PASS alone does not establish which revision is deployed.

**Old recovery work reappears after cleanup.** A recovery incident can dispatch another rescue while its budget remains. Cancelling or deleting the original mission or its latest rescue now closes the recovery chain, with an operator-stop note rather than a claim that a fix landed. On older images, close the related recovery incident before stopping and deleting its missions; deleting only the voyage leaves standalone rescues outside that voyage.

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
runtimes other than Claude Code, Mux 1.5.0 or later, and ApiEndpoint, on Harbors that do not advertise the
`cli-permission-prompt` capability for the runtime (update the Harbor, and its `mux`), and for Claude Code or Mux
without a scoped MCP token). Pending requests are listed at
Settings > CLI Tool Permissions (`/server?tab=cli-permissions`; the old `/cli-permissions` URL redirects there) in the dashboard and in the TUI Approvals center; an undecided request is denied after
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

## Persona prompts and stage handoffs

Built-in persona briefs live in `src/Armada.Core/Prompts/persona-briefs.json`. Each role keeps its existing result/verdict protocol. Architects plan the fewest useful missions with measurable acceptance criteria; Workers resolve original requirements and prior findings; Judges distinguish blocking defects from optional improvements and recheck every prior finding.

Startup upgrades exact, untouched legacy defaults and preserves customized templates. Reset a template to adopt the current default deliberately. Prompt-template API updates apply to newly generated instructions; running jobs keep their existing instructions. Changing a persona prompt does not change its captain priorities.

Stage handoffs carry source mission, branch, commit, and at most 3,000 characters of prior output (opening and closing excerpts). Complete output and diffs remain persisted on the prior mission; agents must retrieve omitted evidence before resolving findings. Template diff excerpts are capped at 4,000 characters. These bounds limit copied context; they do not truncate the mission's original acceptance criteria or stored artifacts.
