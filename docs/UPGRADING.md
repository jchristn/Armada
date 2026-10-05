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
| Docker ([DOCKER.md](DOCKER.md)) | `git pull`, then `docker/update.sh` (or `docker\update.bat`), optionally with the compose file, for example `docker/update.sh armada/compose.split.yaml`. The compose files build the Admiral and dashboard from the checkout, so the script pulls the observability images, recreates the stack with `--build`, and keeps `db/`, `logs/`, and the named volumes. |
| Packages (`.msi`, Inno, `.pkg`, Deb/Rpm) | Install the new package over the old one. The package re-registers the service (see "Service and startup registration" in [OPERATIONS.md](OPERATIONS.md)). |

Use the update script that matches how you installed; they are not interchangeable (for example `update.bat`
updates the global-tool install and does not touch the `ArmadaAdmiral` startup entry).

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
| `jobRetentionDays` | 30 | 0 to 3650 | Delete finished background jobs (Succeeded, Failed, Cancelled) older than this. The newest finished job of each kind and name per tenant is kept, because schedules such as vessel health measure from it. |
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
