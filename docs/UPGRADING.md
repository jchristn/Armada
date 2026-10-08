# Upgrading, Backups, and Restores

This guide covers how an Armada Admiral moves its database forward between releases, what it does to protect your data on the way, and how to get back to a known-good state when you need to. It also lists the retention settings that keep long-running installs from growing without bound.

## Supported upgrade paths

Upgrade any 0.9.x release directly to 1.0. You do not need to step through intermediate builds: the Admiral applies every pending schema migration, in order, the first time the new version starts.

The 0.9.x line shipped over several weeks and the schema moved a lot inside it, so the upgrade test runs against two 0.9.0 builds: the `release(v0.9.0)` commit `e456b008` (schema v44 on SQLite) and `574a8a1a`, the commit the published v0.9.0 container images were built from (schema v69). Both upgrade cleanly on SQLite, PostgreSQL, MySQL, and SQL Server. See [Testing an upgrade](#testing-an-upgrade).

Releases before 0.9.0 are not covered by the test. Their schemas are older versions of the same migration history, so a direct upgrade normally works, but if you are on 0.8.x or earlier, upgrade to 0.9.x first, confirm it starts, and then move to 1.0.

Downgrades are not supported. If you start an older Admiral against a database that a newer one already migrated, it logs a warning naming both schema versions and does not try to migrate. Restore the backup you took before the upgrade instead (the steps are below).

## Upgrading an install

Back up first (see [Backups during normal operation](#backups-during-normal-operation), or dump a server database),
then replace the binaries the way you installed them. The new Admiral migrates the database on its first start.

| Install type | How to upgrade |
|--------------|----------------|
| Source checkout with the `armada` global tool | `git pull`, then `scripts/linux/update.sh`, `scripts/macos/update.sh`, or `scripts\windows\update.bat`. The script stops repo-backed MCP stdio hosts and the server, reinstalls the `Armada.Helm` tool, redeploys the dashboard, and starts the server again. |
| Startup scripts ([RUN_ON_STARTUP.md](RUN_ON_STARTUP.md)) | `git pull`, then `update-systemd-user.sh`, `update-launchd-agent.sh`, or `update-windows-task.bat`. These republish `~/.armada/bin` and restart the registered server. |
| Docker ([DOCKER.md](DOCKER.md)) | `git pull`, then `docker/update.sh` (or `docker\update.bat`), optionally with the compose file, for example `docker/update.sh armada/compose.split.yaml`. The compose files name release tags (`jchristn77/armada-server:v1.0.0` and so on), so the script pulls the tags the pulled checkout names, recreates the stack, and keeps `db/`, `logs/`, and the named volumes. |
| Packages (`.msi`, Inno, `.pkg`, Deb/Rpm) | Install the new package over the old one. The package re-registers the service (see "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md)). |

Use the update script that matches how you installed; they are not interchangeable (for example `update.bat`
updates the global-tool install and does not touch the `ArmadaAdmiral` startup entry).

### Changes to act on when coming from 1.0.0

1.0.1 fixes the landing modes. `LocalMerge` now merges into the working directory without pushing; the new
`MergeAndPush` mode merges and pushes (what `LocalMerge` did in 1.0.0) and is the global default. Nothing is
migrated: switch every vessel, voyage, and global `landingMode` that should keep pushing from `LocalMerge` to
`MergeAndPush`. See [Upgrading from 1.0.0](MERGING.md#upgrading-from-100-landing-modes-fixed-in-101) for the steps.

### Changes to act on when coming from 0.9.x

1.0.0 tightened several defaults. Check these before you start the new version (the CHANGELOG has the full list):

- **Default credentials.** The first sign-in as `admin@armada` with the default password must set a new password,
  which also disables the `default` bearer token. The Admiral refuses to start on a non-loopback `rest.hostname`
  while default credentials are in use unless `allowDefaultCredentialsOnNetwork` is `true`; set
  `ARMADA_INITIAL_ADMIN_PASSWORD` (8+ characters) on the first start of a headless or Docker install. Docker compose
  requires it.
- **MCP and WebSocket authentication.** MCP calls need a credential unless the listener is bound to loopback and
  `mcp.allowUnauthenticatedLoopback` is `true` (the default). `/ws` requires authentication; custom clients pass
  `?token=<token>`.
- **Server control.** `POST /api/v1/server/stop`, `restart`, `rebuild`, and `rollback` always require an admin;
  `requireAuthForShutdown` is deprecated and ignored.
- **Docker.** The images run as non-root (Admiral and proxy UID 1654, dashboard UID 101 on port 8080); make the
  bind-mounted directories writable with `sudo chown -R 1654:1654 docker/armada/db docker/armada/logs`.
- **Armada.Proxy.** The proxy refuses to start with a blank or default password; set `ARMADA_PROXY_PASSWORD` (or
  `password` in `proxysettings.json`) and the same value as `remoteControl.password` on each instance.
- **Passwords.** Stored hashes are upgraded automatically (see [Password hashes](#password-hashes)); an upgraded
  database cannot be used for password login by an older Admiral.
- **Removed.** The keyword `POST /api/v1/ask` responder and the `armada ask` command are gone; use Ask Armada threads.

## How an upgrade runs

The schema version lives in the `schema_migrations` table: one row per applied migration. At startup the Admiral compares the highest recorded version with the newest migration it ships. Before it applies anything, it protects the existing data:

| Provider | What happens before migrating |
|----------|-------------------------------|
| SQLite | The database file and its `-wal` and `-shm` files are copied to `{DataDirectory}/backups/pre-migration-<UTC timestamp>-v<from>-to-v<to>/`. The log line names the folder and the rollback steps. |
| PostgreSQL, MySQL, SQL Server | Armada cannot copy a server database itself. It logs a prominent `DATABASE UPGRADE` warning with the dump command for your provider and, if you asked it to, refuses to migrate until you confirm a backup exists. |

Nothing happens on a brand-new database (there is nothing to protect) or on one that is already current.

Each migration then runs in a transaction and records its version in the same transaction. SQLite, PostgreSQL, and SQL Server roll back DDL with the transaction; MySQL does not, so a crash in the middle of a MySQL migration can leave part of it applied. That case is safe because every migration is re-runnable (see [Migration guarantees](#migration-guarantees)): the next start applies the migration again and finishes it.

After migrating, startup seeds anything the new version ships that your database lacks: built-in prompt templates, personas, pipelines, fleet actions. Built-in records you never changed pick up the new defaults (for example the Recorder stage on the built-in `FullPipeline`, and the memory-recall section on working persona templates). Records you edited keep your version. The one exception is the built-in `FullPipeline`: its stage list is reset to the shipped definition whenever it differs, so copy it to a custom pipeline if you want different stages.

### SQLite pre-migration backups

The SQLite copy is a plain file copy taken while nothing has the database open, so the folder is a complete, consistent database you can put back by copying the files. By default the newest 5 pre-migration backups are kept and older `pre-migration-*` folders are deleted after each new one. Nothing else in `backups/` is touched.

| Setting (`settings.json`) | Default | Range | Meaning |
|---------------------------|---------|-------|---------|
| `database.migrationBackupRetentionCount` | 5 | 1 to 100 | Pre-migration backup folders to keep. |

If the copy fails (a full disk, for example), startup stops with the error instead of migrating without a backup.

### Server providers: dump first

For PostgreSQL, MySQL, and SQL Server, take a backup with your provider's tools before you start the new version. The startup warning prints a command filled in with your host, port, user, and database; the general forms are:

```bash
# PostgreSQL (custom format, restore with pg_restore)
pg_dump -h <host> -p <port> -U <user> -Fc -f armada-before-schema-v<N>.dump <database>

# MySQL
mysqldump -h <host> -P <port> -u <user> -p --single-transaction --routines --triggers <database> > armada-before-schema-v<N>.sql

# SQL Server (the path is on the database server)
sqlcmd -S <host>,<port> -U <user> -C -Q "BACKUP DATABASE [<database>] TO DISK = N'armada-before-schema-v<N>.bak' WITH INIT, CHECKSUM"
```

By default the Admiral warns and migrates. Operators who want a hard stop can make startup wait for an explicit confirmation:

| Setting (`settings.json`) | Default | Meaning |
|---------------------------|---------|---------|
| `database.requireBackupConfirmationForMigrations` | `false` | When `true`, refuse to migrate a server database until a backup is confirmed for the target schema version. |
| `database.confirmedBackupForSchemaVersion` | `0` | The schema version you confirmed a backup for. Startup proceeds when this is at least the target version. |

With the requirement on and no confirmation, the Admiral logs the dump command, prints `Startup refused: ...` with the same instructions, and exits with code 3. Take the dump, then either set `database.confirmedBackupForSchemaVersion` to the target version shown in the message, or set the `ARMADA_CONFIRM_BACKUP_FOR_SCHEMA_VERSION` environment variable to it (handy for Docker and one-off runs), and start again. The confirmation names a version on purpose: confirming the backup for one upgrade never approves the next one.

## Backups during normal operation

For SQLite, `GET /api/v1/backup` (the Backup button on the dashboard Server page, or the `backup` MCP tool) writes a ZIP to `{DataDirectory}/backups/armada-backup-<timestamp>.zip` and streams it back. The ZIP holds a consistent snapshot of the database taken with SQLite's online backup API (so it includes writes still in the WAL), the `settings.json` the server loaded, and a `manifest.json` with the schema version, Armada version, and record counts. Taking one is safe while Armada is running.

The built-in backup does not apply to server providers; the endpoint returns 400 and points here. Use the dump commands above on whatever schedule your database team already runs.

## Restoring

Restore whichever copy matches the version you are going to run. A backup taken before an upgrade has the old schema: either start the old version on it, or start the new version and let it migrate again (it will take a fresh pre-migration copy first).

### SQLite: from a backup ZIP

Use `POST /api/v1/restore` with the ZIP as the request body (or the Restore button on the Server page). The Admiral first writes a safety backup of the current state to `{DataDirectory}/backups/pre-restore-<timestamp>.zip`, then copies the backup into the live database with the SQLite online backup API and replaces `settings.json` if the ZIP has one. Restart the Admiral afterward so every component reloads from the restored data. An upload that is not an Armada backup (no `armada.db` with a `schema_migrations` table) is rejected and nothing changes.

The restore is covered by an automated drill (`E2E.BackupRestoreDrill`): back up through the API, add, rename, and delete records, restore through the API, restart, and check the data matches the backup exactly.

### SQLite: from a pre-migration backup folder

1. Stop the Admiral.
2. Copy the files from `{DataDirectory}/backups/pre-migration-.../` over the live database: `armada.db`, plus `armada.db-wal` and `armada.db-shm` if the folder has them. If the folder has no `-wal` file, delete any `armada.db-wal` and `armada.db-shm` next to the live database so a stale write-ahead log is not replayed onto the restored file.
3. Start the version you want to run.

The same steps work for any copy of the database files you took yourself while the Admiral was stopped.

### PostgreSQL

Stop the Admiral, then restore into an empty database (drop and recreate it, or restore into a new one and point `database.databaseName` at it):

```bash
dropdb -h <host> -p <port> -U <user> <database>
createdb -h <host> -p <port> -U <user> <database>
pg_restore -h <host> -p <port> -U <user> -d <database> --no-owner armada-before-schema-v<N>.dump
```

### MySQL

```bash
mysql -h <host> -P <port> -u <user> -p -e "DROP DATABASE <database>; CREATE DATABASE <database>;"
mysql -h <host> -P <port> -u <user> -p <database> < armada-before-schema-v<N>.sql
```

### SQL Server

```bash
sqlcmd -S <host>,<port> -U <user> -C -Q "ALTER DATABASE [<database>] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [<database>] FROM DISK = N'armada-before-schema-v<N>.bak' WITH REPLACE; ALTER DATABASE [<database>] SET MULTI_USER;"
```

For every server provider, start the Admiral once the restore finishes. If the restored schema is older than the build you start, it migrates again (with the warning, and the confirmation if you require one).

## Password hashes

Starting with the 1.0 security release, user passwords are stored as salted PBKDF2-HMAC-SHA256 instead of unsalted
SHA-256. No migration or user action is needed: the Admiral rewrites every legacy hash when it starts (it stretches the
stored SHA-256 value, so every password keeps working), and any hash it could not rewrite is upgraded on that user's
next successful login. Once upgraded, the database cannot be used for password login by an older Admiral; restore the
pre-upgrade backup if you need to roll back.

## Migration guarantees

Every migration on every provider is held to two rules, and `Database.MigrationHygiene` checks both on each test run (the database parity script runs it on all four providers):

- **Re-runnable.** Applying the full migration list a second time on a migrated, populated database succeeds and changes neither the schema nor the data. Statements use `IF NOT EXISTS`-style guards, and the few that cannot (the SQLite v15 table rebuild, the SQL Server v1 initial schema) declare a check that skips them once the schema already contains their change.
- **Additive.** Migrations add tables, columns, and indexes and backfill values that are still empty. They do not drop, rename, or delete. The only destructive statements are two reviewed SQLite migrations from before the rule existed (the v8 removal of an unused `captains.max_parallelism` column and the v15 multi-tenant table rebuild, which copies every row); the check fails on anything new until it is reviewed.

Migration version numbers increase but are not contiguous, and they differ per provider; each provider's `schema_migrations` table is the source of truth for that database.

## Data retention

Long-running installs accumulate conversations, job records, and import history. Retention runs in the background on the health-check loop's slow cadence (every 100 health-check cycles, about every 17 minutes with the default 10-second `heartbeatIntervalSeconds`), works on every provider, and applies setting changes immediately. Set any value to 0 to keep that data forever.

| Setting (`retention` in `settings.json`, the Server page, or `PUT /api/v1/settings`) | Default | Range | Effect |
|---|---|---|---|
| `askThreadArchiveAfterDays` | 90 | 0 to 3650 | Archive Ask threads with no activity (last message, or creation when empty) for this many days. Pinned threads are never archived. |
| `askThreadDeleteAfterDays` | 0 (never) | 0 to 3650 | Delete Ask threads idle this long, archived or not, with their messages, tool calls, proposals, and tracked work. Pinned threads are never deleted. |
| `jobRetentionDays` | 30 | 0 to 3650 | Delete finished background jobs (Succeeded, Failed, Cancelled) older than this. The newest finished job of each kind and name per tenant is kept, because schedules such as vessel health measure from it. Harbor metrics (Harbor job records, link samples, and link events) follow the same retention. |
| `importBatchRetentionDays` | 90 | 0 to 3650 | Delete finished vessel import batches (Completed, CompletedWithFailures, Failed) with their items and fleet recommendations. Imported vessels are not affected; batches still categorizing are kept. |

Other data has its own controls. Fleet action runs (and their captured output) follow `fleetActions.runRetentionDays` (default 30). Request history follows `requestHistoryRetentionDays` (default 30). Completed voyages and missions, old signals, and old events follow `dataRetentionDays` (default 30), which currently applies to SQLite only. Vessel health findings need no retention: each evaluation replaces the previous findings for a vessel instead of adding history.

## Testing an upgrade

`scripts/common/run-upgrade-test.sh` (or `scripts\windows\run-upgrade-test.bat`, which runs the same script through Git Bash) reproduces a real upgrade. It extracts the baseline source with `git archive`, builds a small host for it, starts the old Admiral against a throwaway database with a private home directory, seeds data through its REST API (fleets, vessels, a captain, a mission in every status, voyages, merge queue entries, edited personas and pipelines, prompt template overrides, signals, a backlog objective, and a second tenant with its own credential), stops it, and hands the database to the current build. The `Upgrade.FromBaseline` suite then checks the pre-migration backup, the migration, every seeded record, the tenant credential, that edits survived, and that built-in upgrades were applied.

```bash
scripts/common/run-upgrade-test.sh                          # SQLite, baseline v0.9.0
scripts/common/run-upgrade-test.sh --providers all          # plus PostgreSQL, MySQL, SQL Server in Docker
scripts/common/run-upgrade-test.sh --from-ref 574a8a1a      # any other baseline commit or tag
```

The baseline defaults to the `v0.9.0` tag when the repository has one, and to the `release(v0.9.0)` commit `e456b008` otherwise. Run it before every release candidate.

## Version-by-version upgrade notes

What changed in `settings.json`, the database schema, and behavior at each release step, oldest first. Schema migrations run automatically at startup; the steps below are the ones that may need you.

### v0.1.0 to v0.2.0

**Breaking change:** The `settings.json` format changed. Armada v0.2.0 will fail to start with a v0.1.0 `settings.json`.

The `databasePath` string property was replaced with a `database` object supporting multiple backends (SQLite, PostgreSQL, SQL Server, MySQL).

#### Before (v0.1.0)

```json
{
  "databasePath": "armada.db",
  "admiralPort": 7890,
  "maxCaptains": 5
}
```

#### After (v0.2.0)

```json
{
  "database": {
    "type": "Sqlite",
    "filename": "armada.db"
  },
  "admiralPort": 7890,
  "maxCaptains": 5
}
```

#### Minimal change for SQLite users

Replace:

```json
"databasePath": "path/to/armada.db"
```

With:

```json
"database": {
  "type": "Sqlite",
  "filename": "path/to/armada.db"
}
```

No other changes are required -- all other settings remain the same.

#### Switching to PostgreSQL

```json
"database": {
  "type": "Postgresql",
  "hostname": "localhost",
  "port": 5432,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "schema": "public",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Switching to SQL Server

```json
"database": {
  "type": "SqlServer",
  "hostname": "localhost",
  "port": 1433,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Switching to MySQL

```json
"database": {
  "type": "Mysql",
  "hostname": "localhost",
  "port": 3306,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Additional notes

- **Port auto-detection:** Setting `port` to `0` (or omitting it) auto-detects the default port for each database type (PostgreSQL: 5432, SQL Server: 1433, MySQL: 3306).
- **Connection pooling:** All non-SQLite backends support connection pooling via `minPoolSize` (0-100), `maxPoolSize` (1-200), `connectionLifetimeSeconds` (minimum 30), and `connectionIdleTimeoutSeconds` (minimum 10).
- **Encryption:** Set `requireEncryption` to `true` to require encrypted connections for PostgreSQL, SQL Server, or MySQL.
- **Backup/restore:** The `backup` and `restore` MCP tools are only available when using SQLite. If you switch to PostgreSQL, SQL Server, or MySQL, use your database's native backup tools instead.

#### Automated migration script

For existing v0.1.0 deployments, run the migration script to automatically convert your `settings.json`:

**Windows:**
```
migrations\migrate_v0.1.0_to_v0.2.0.bat
# or with a custom path:
migrations\migrate_v0.1.0_to_v0.2.0.bat C:\path\to\settings.json
```

**Linux/macOS:**
```
./migrations/migrate_v0.1.0_to_v0.2.0.sh
# or with a custom path:
./migrations/migrate_v0.1.0_to_v0.2.0.sh /path/to/settings.json
```

The script backs up your original file to `settings.json.v0.1.0.bak` before making changes.

**Requires:** jq (Linux/macOS) -- install via `apt install jq`, `brew install jq`, etc.

### v0.2.0 to v0.3.0

v0.3.0 introduces multi-tenant support. The database schema is automatically migrated on first startup. Key changes:

- **New tables:** `TenantMetadata`, `UserMaster`, `Credential` are created automatically
- **Default data seeded:** A default tenant (`default`), user (`admin@armada` / `password`), and credential (bearer token `default`) are created if no tenants exist
- **All operational tables gain `TenantId`:** Existing rows are assigned to the `default` tenant during migration
- **All operational tables gain `UserId`:** Existing rows are assigned to the earliest user in their tenant during migration
- **Ownership integrity:** Operational `TenantId` and `UserId` columns are indexed and protected by database foreign keys across all supported backends
- **Protected auth resources:** The default tenant, its default user/credential, and the synthetic system records are seeded as protected and cannot be deleted directly
- **Role model:** `IsAdmin` now means global system admin. `IsTenantAdmin` means tenant-scoped admin. Regular users are limited to their own tenant, own account, and own credentials
- **Password management:** User create/update APIs accept plaintext `Password`; the server hashes it before persistence. Leaving `Password` blank on update preserves the existing password. The dashboard exposes this through the Users edit modal for both admin-managed and self-service password changes
- **Protected resources:** `IsProtected` is server-controlled on tenants, users, and credentials. Protected objects cannot be deleted directly, and immutable identifiers/timestamps/ownership fields are preserved on update
- **Tenant-created seed admin:** Creating a tenant also creates `admin@armada` with password `password` plus a default credential inside that tenant; that seeded user is tenant admin only (`IsAdmin = false`, `IsTenantAdmin = true`) and those child resources are protected from direct delete
- **Authentication required:** All REST API endpoints now require authentication. Use `Authorization: Bearer default` for backward-compatible access
- **`X-Api-Key` deprecated:** The `X-Api-Key` header still works but is deprecated. If configured, it maps to a synthetic admin identity. Migrate to bearer tokens
- **New settings:** `AllowSelfRegistration` (default then: `true`; 1.0 defaults to `false`), `RequireAuthForShutdown` (default: `false`), `SessionTokenEncryptionKey` (auto-generated)

No manual changes to `settings.json` are required. Existing `ApiKey` settings continue to work.

### v0.3.0 to v0.4.0

v0.4.0 adds personas, pipelines, and prompt templates. The database schema is automatically migrated on first startup (migrations 19-23). Key changes:

- New tables: `prompt_templates`, `personas`, `pipelines`, `pipeline_stages`
- New columns: `captains.allowed_personas`, `captains.preferred_persona`, `missions.persona`, `missions.depends_on_mission_id`, `fleets.default_pipeline_id`, `vessels.default_pipeline_id`
- Built-in personas (Worker, Architect, Judge, TestEngineer) and pipelines (WorkerOnly, Reviewed, Tested, FullPipeline) are seeded automatically
- 18 built-in prompt templates are seeded automatically
- Standalone migration scripts available in `migrations/` for manual execution

### v0.4.0 to v0.5.0

v0.5.0 is focused on dispatch and pipeline stability. It adds captain model selection, startup model validation, mission runtime tracking, and a broad set of handoff, landing, cleanup, and workflow reliability improvements. The database schema is automatically migrated on first startup (migrations 24-27). Key changes:

- New columns: `captains.model`, `missions.total_runtime_ms`
- Captain model overrides are persisted across SQLite, MySQL, PostgreSQL, and SQL Server
- REST and MCP captain create/update operations validate configured models before saving
- React dashboard captain detail now exposes the captain model field and shows validation errors in a modal
- Mission detail now shows total runtime, and dispatch cleanup removes the redundant parsed-task UI
- Docker image tags, release metadata, and API documentation are updated for `v0.5.0`

### v0.6.0 to v0.7.0

v0.7.0 is focused on remote access. This release adds the local outbound tunnel client, the first shipped `Armada.Proxy` service, tunnel telemetry, server/dashboard configuration surfaces, and a bounded remote management shell for day-one operator workflows. No database schema migration is required for this release.

Key changes:

- New `RemoteControl` settings in `settings.json`, exposed through `GET /api/v1/settings` and `PUT /api/v1/settings`
- New `RemoteTunnel` health/status telemetry, exposed through `/api/v1/status`, `/api/v1/status/health`, the React dashboard, the legacy dashboard, and `armada status`
- Experimental outbound websocket tunnel client with URL normalization, handshake, heartbeat, reconnect, request/response handling, and event forwarding
- New `Armada.Proxy` service with websocket tunnel termination, a mobile-first remote operations shell, focused instance inspection APIs, live forwarded status/health/detail requests, and the initial bounded remote-management slice for fleets, vessels, voyages, missions, and captain stop
- The embedded server host now runs on Watson Webserver 7 for both HTTP and WebSocket traffic, replacing the standalone `WatsonWebsocket` dependency and fixing foreground startup handoff
- The dashboard setup wizard was rebuilt into a contained first-run workflow with direct dispatch, richer guidance, and improved server/settings ergonomics
- Dashboard internationalization now includes login language selection, persistent locale preference, route-level React coverage, legacy embedded dashboard coverage, and locale-aware date/time/number formatting
- New operator docs: `docs/REMOTE_MGMT.md`, `docs/TUNNEL_PROTOCOL.md`, `docs/PROXY_API.md`, and `docs/TUNNEL_OPERATIONS.md`
- Release metadata, Docker image tags, Postman examples, and API documentation are updated for `v0.7.0`
- Standalone no-op release scripts are available in `migrations/` for `v0.6.0 -> v0.7.0`

### v0.7.0 to v0.8.0

v0.8.0 is focused on backlog-first delivery management. This release adds normalized objective storage, explicit backlog refinement sessions with captain selection, ranked backlog management, and end-to-end linkage from backlog items into release, deployment, and incident records. The Armada server applies the required schema migration (startup migration 43) automatically on first startup across SQLite, PostgreSQL, MySQL, and SQL Server.

Key changes:

- New normalized `objectives`, `objective_refinement_sessions`, and `objective_refinement_messages` persistence across SQLite, MySQL, PostgreSQL, and SQL Server
- Objective/backlog CRUD, filtering, ranking, reorder, and backlog alias routes under `/api/v1/backlog`
- Backlog refinement sessions with explicit captain selection, transcript persistence, summary generation, and objective apply-back support
- MCP backlog CRUD and reorder coverage, plus backlog-named aliases for first-class backlog operations
- Release, deployment, and incident flows now preserve linkage back to the same objective record
- Shared version metadata, Postman examples, and current-version API docs are updated for `v0.8.0`
- Versioned migration handoff scripts are available in `migrations/` for `v0.7.0 -> v0.8.0`

### v0.8.0 to v0.9.0

v0.9.0 is focused on reliability: it eliminates the stuck-dock and dangling-handoff failure modes and hardens the orchestrator for multi-instance operation. The Armada server applies startup migration 44 automatically on first startup across SQLite, PostgreSQL, MySQL, and SQL Server.

Key changes:

- Fixed stall detection (process liveness is tracked separately from the output heartbeat, so a live-but-silent agent is still caught) plus a configurable max-mission-runtime backstop for runaways
- Cross-platform process supervision with PID-identity verification, and automatic re-drive of dangling pipeline handoffs each health cycle
- Review-timeout watchdog, an enforced global `MaxConcurrentMissions` ceiling, and non-destructive dock repair/unstick operator tools (REST + MCP)
- Merge queue background driver with hard subprocess timeouts and multi-instance-safe processing via a durable coordination lease
- Centralized, tested mission state machine (single authoritative transition table and classifiers)
- Startup migration 44 adds dock state/lease, captain process-liveness, mission review deadline, merge-entry retry/lease, and a durable coordination-lease table
- Opt-in OpenTelemetry export (OTLP collector, in-process Prometheus scrape, and/or Loki); the Docker stack ships Prometheus, Loki, and Grafana with an "Armada Reliability" dashboard
- Shared version metadata, Postman examples, and current-version API docs are updated for `v0.9.0`
- Versioned migration handoff scripts are available in `migrations/` for `v0.8.0 -> v0.9.0`

### v0.9.0 to v1.0.0

v1.0.0 is the first stable release: security hardening, a frozen and documented API surface, upgrade safety, Ask Armada as the home base, the terminal UI, Harbors, and install packages for every platform. Upgrade any 0.9.x release directly; on 0.8.x or earlier, move to 0.9.x first. Downgrades are not supported. The full procedure, backups, and restores are described above; every change is in [CHANGELOG.md](../CHANGELOG.md).

**Database**

- The Admiral applies every pending migration on first start, through migration 77, on SQLite, PostgreSQL, MySQL, and SQL Server (Harbors, agent memory, vessel import, fleet actions, vessel health, Ask threads, per-vessel auto-approve, and mission failure kinds, among others). Every migration is safe to re-run.
- SQLite is backed up automatically before migrating, to `{DataDirectory}/backups/pre-migration-*` (newest 5 kept, `database.migrationBackupRetentionCount`). On a server provider take a dump first: the Admiral logs the command, and `database.requireBackupConfirmationForMigrations` makes it refuse to migrate until you confirm a backup.
- Passwords are re-hashed as salted PBKDF2-SHA256 on first start; an upgraded database cannot be used for password login by an older Admiral.

**Security and access**

- **Default credentials:** the default admin password is flagged, not blocked (the dashboard prompts for a new one; the API and TUI keep working with a warning). Changing it disables `Authorization: Bearer default`. The Admiral refuses to listen on a non-loopback hostname while default credentials are in use unless `AllowDefaultCredentialsOnNetwork` is true; Docker compose requires `ARMADA_INITIAL_ADMIN_PASSWORD`.
- **MCP:** unauthenticated calls are accepted only when the Admiral listens on localhost (`Mcp.AllowUnauthenticatedLoopback`, default true); remote MCP clients must send a credential. `backup`, `restore`, and `stop_server` need an admin credential even locally. Tool calls are rate limited per client (`mcp.toolCallsPerSecond`, default 100). Custom MCP clients must perform the `initialize` / `Mcp-Session-Id` handshake.
- **Server control:** `POST /api/v1/server/stop`, `restart`, `rebuild`, and `rollback` always require an admin; `RequireAuthForShutdown` is ignored. The `armada` CLI sends the local API key.
- **WebSocket:** `/ws` requires authentication (non-browser clients pass `?token=<token>`), events are scoped to the tenant (and `ask.*` events to the owning user), and WebSocket commands are global-admin only.
- **Self-registration** defaults to `false` for new settings files. **Credentials:** bearer tokens are shown once at creation and masked on reads. **Logins** are rate limited (`loginRateLimit`, 429 with `Retry-After`).
- **Permissions:** every route and tool declares its authorization; check-run writes and Harbor probes need a tenant admin; `POST .../enumerate` routes need only authentication. See [SECURITY_REVIEW.md](SECURITY_REVIEW.md#permission-changes-in-w1).
- **Harbor:** a Harbor connecting from another host must send an Armada credential as its access key.
- **Proxy:** Armada.Proxy refuses to start with a blank or default password (compose requires `ARMADA_PROXY_PASSWORD`).
- **Docker:** containers run as non-root (UID 1654 for the Admiral and proxy, 101 for the dashboard, which now listens on 8080). Make bind-mounted `db` and `logs` directories writable by UID 1654.

**API behavior (scripts and integrations)**

- REST errors always use `ApiErrorResponse` with an `Error` code matching the HTTP status. A missing entity referenced in a create or update body is now 404 (was 400); planning and refinement routes answer 404 for a missing captain, vessel, or dock (was 409) and 400 for invalid input (was 500); deletes answer 409 for a blocking state (was 404); cross-tenant reads of users, prompt templates, memories, model endpoints, and harbors answer 404 (was 403); several validation errors that returned 200 now return 400 or 404.
- MCP tool errors carry a typed `ErrorCode` (`NotFound`, `InvalidArgument`, `Conflict`, `Forbidden`, `Unavailable`, `Failed`). Missing entities that used to return an untyped error now return `NotFound`.
- The 1.0 surface is frozen in [API_SURFACE_1.0.md](API_SURFACE_1.0.md) and covered by [COMPATIBILITY.md](COMPATIBILITY.md). Harbor split mode and self-rebuild are experimental and excluded.
- **Removed:** `POST /api/v1/ask` and `armada ask`. Use Ask Armada conversations (`/api/v1/ask/threads`).

**CLI**

- `armada go` takes a repeatable `--task` (`-t`) for multiple missions and never splits a prompt on `;` or `1.`; without `--task` the whole prompt is one mission.
- `--runtime` (`captain add`, `captain update`, `config set DefaultRuntime`) is validated: `opencode` and `api` now work, and an unknown value is an error instead of silently creating a Claude Code captain.
- New: `armada tui`, `armada health`, `armada action ...`, `armada vessel import`.

**Missions, agents, and prompt templates**

- Judges must end with a standalone `[ARMADA:VERDICT] PASS`, `FAIL`, or `NEEDS_REVISION` line (outside a code block). "Verdict: PASS" prose and bare PASS/FAIL lines no longer count. If you edited the Judge persona template, make sure it still asks for that line.
- Architects emit their plan as a fenced `armada-plan` JSON block (`[ARMADA:MISSION]` blocks are still accepted). `[ARMADA:STATUS]` can only move a mission between InProgress and Testing.
- Missions carry a typed `FailureKind` (`MissionFailureKindEnum`), and auto-rescue decides on it. `FailureReason` is plain text without the old prefixes. Failures recorded before the upgrade have no kind and are not auto-rescued.
- Runtime failures are classified from exit codes and structured provider errors, not by searching output text, so a build error mentioning "403" no longer quarantines a captain.
- A vessel with Landing Mode `None` stops at WorkProduced and `MergeQueue` enqueues; neither merges into the vessel's working directory any more.
- Codex captains run with `--sandbox workspace-write` (codex 0.159 removed `--full-auto`).

**New settings worth reviewing:** `mcp.toolCallsPerSecond`, `mcp.missionScopedTokens` (default true), `mcp.allowUnauthenticatedLoopback` (default true), `ask.*` (including `captainAutoApprove`, default false), `retention.*` (Ask threads archive after 90 idle days, finished jobs deleted after 30), `loginRateLimit`, `database.migrationBackupRetentionCount`, `database.requireBackupConfirmationForMigrations`, and the per-vessel `AutoApprove` override.

### v1.0.0 to v1.0.1

**Landing modes changed; check yours.** `LocalMerge` no longer pushes: it merges finished work into the vessel's working directory and stops there. The new `MergeAndPush` mode merges and then pushes (what `LocalMerge` did in 1.0.0), and it is the global default. Nothing is migrated, so switch any vessel, voyage, or global `landingMode` that should keep pushing from `LocalMerge` to `MergeAndPush`. The `autoPush`/`autoCreatePullRequests` settings, the "Auto-Create Pull Requests" toggle, and `armada go --push/--pr/--merge` are replaced by landing modes (`armada go --landing-mode`, `armada config set landingMode`). Step-by-step instructions: [MERGING.md](MERGING.md#upgrading-from-100-landing-modes-fixed-in-101).
