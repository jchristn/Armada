@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: v0.2.0 to v0.3.0
echo ====================================
echo.
echo This script performs two migrations:
echo   1. Database: creates auth tables, backfills tenant_id on all records
echo   2. Settings: adds new v0.3.0 settings with defaults
echo.

REM Check for sqlite3
where sqlite3 >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: sqlite3 is required but not found on PATH.
    echo Download from https://www.sqlite.org/download.html
    exit /b 1
)

REM Check for PowerShell
where powershell >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: PowerShell is required but not found on PATH.
    exit /b 1
)

REM Determine settings file path
if "%~1"=="" (
    set "SETTINGS_FILE=%USERPROFILE%\.armada\settings.json"
) else (
    set "SETTINGS_FILE=%~1"
)

echo Settings file: %SETTINGS_FILE%

REM Check if the file exists
if not exist "%SETTINGS_FILE%" (
    echo.
    echo ERROR: Settings file not found: %SETTINGS_FILE%
    echo.
    echo Usage: %~nx0 [path\to\settings.json]
    echo Default path: %USERPROFILE%\.armada\settings.json
    exit /b 1
)

REM Determine database path from settings
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "$json = Get-Content '%SETTINGS_FILE%' -Raw | ConvertFrom-Json; if ($json.database -and $json.database.filename) { $json.database.filename } elseif ($json.databasePath) { $json.databasePath }"`) do set "DB_PATH=%%i"

if "!DB_PATH!"=="" (
    set "DB_PATH=%USERPROFILE%\.armada\armada.db"
)

echo Database file: !DB_PATH!

if not exist "!DB_PATH!" (
    echo.
    echo ERROR: Database file not found: !DB_PATH!
    echo Make sure the Armada server has been run at least once to create the database.
    exit /b 1
)

REM ========================================
REM Part 1: Database Migration
REM ========================================
echo.
echo --- Database Migration ---
echo.

REM Create backup
set "DB_BACKUP=!DB_PATH!.v0.2.0.bak"
copy /y "!DB_PATH!" "!DB_BACKUP!" >nul
echo Database backup created: !DB_BACKUP!

REM Create auth tables and seed data
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS tenants (id TEXT PRIMARY KEY, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL);"
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, email TEXT NOT NULL, password_sha256 TEXT NOT NULL, first_name TEXT, last_name TEXT, is_admin INTEGER NOT NULL DEFAULT 0, is_tenant_admin INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE);"
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS credentials (id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, user_id TEXT NOT NULL, name TEXT, bearer_token TEXT NOT NULL UNIQUE, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE, FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE);"

REM Auth table indexes
sqlite3 "!DB_PATH!" "CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenant_email ON users(tenant_id, email);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_users_tenant ON users(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_credentials_tenant ON credentials(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_credentials_user ON credentials(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_credentials_bearer ON credentials(bearer_token);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_tenants_active ON tenants(active);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_credentials_tenant_user ON credentials(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_credentials_active ON credentials(active);"

echo Auth tables created.

REM Seed default tenant, user, credential
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO tenants (id, name, active, created_utc, last_update_utc) VALUES ('default', 'Default Tenant', 1, datetime('now'), datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO users (id, tenant_id, email, password_sha256, is_admin, is_tenant_admin, active, created_utc, last_update_utc) VALUES ('default', 'default', 'admin@armada', '5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8', 1, 1, 1, datetime('now'), datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO credentials (id, tenant_id, user_id, bearer_token, active, created_utc, last_update_utc) VALUES ('default', 'default', 'default', 'default', 1, datetime('now'), datetime('now'));"

echo Default tenant, user, and credential seeded.

REM Add tenant_id columns (ignore errors if already exist)
for %%T in (fleets vessels captains voyages missions docks signals events merge_entries) do (
    sqlite3 "!DB_PATH!" "ALTER TABLE %%T ADD COLUMN tenant_id TEXT;" 2>nul
)
for %%T in (fleets vessels captains voyages missions docks signals events merge_entries) do (
    sqlite3 "!DB_PATH!" "ALTER TABLE %%T ADD COLUMN user_id TEXT;" 2>nul
)

echo tenant_id columns added to operational tables.

REM Backfill all rows with default tenant
for %%T in (fleets vessels captains voyages missions docks signals events merge_entries) do (
    sqlite3 "!DB_PATH!" "UPDATE %%T SET tenant_id = 'default' WHERE tenant_id IS NULL;"
)
sqlite3 "!DB_PATH!" "ALTER TABLE tenants ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE users ADD COLUMN is_tenant_admin INTEGER NOT NULL DEFAULT 0;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE users ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE credentials ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;" 2>nul
sqlite3 "!DB_PATH!" "UPDATE users SET is_tenant_admin = 1 WHERE is_admin = 1;"
sqlite3 "!DB_PATH!" "UPDATE tenants SET is_protected = 1 WHERE id IN ('default', 'ten_system');"
sqlite3 "!DB_PATH!" "UPDATE users SET is_protected = 1 WHERE id IN ('default', 'usr_system');"
sqlite3 "!DB_PATH!" "UPDATE credentials SET is_protected = 1 WHERE user_id IN ('default', 'usr_system');"
sqlite3 "!DB_PATH!" "UPDATE fleets SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = fleets.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE vessels SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = vessels.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE captains SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = captains.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE voyages SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = voyages.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE missions SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = missions.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE docks SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = docks.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE signals SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = signals.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE events SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = events.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"
sqlite3 "!DB_PATH!" "UPDATE merge_entries SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = merge_entries.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;"

echo All existing records backfilled with tenant_id = 'default'.

set "FK_SQL_FILE=%TEMP%\armada_v030_fk.sql"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$sql = @'
PRAGMA defer_foreign_keys = ON;
ALTER TABLE fleets RENAME TO fleets_old;
ALTER TABLE vessels RENAME TO vessels_old;
ALTER TABLE captains RENAME TO captains_old;
ALTER TABLE voyages RENAME TO voyages_old;
ALTER TABLE missions RENAME TO missions_old;
ALTER TABLE docks RENAME TO docks_old;
ALTER TABLE signals RENAME TO signals_old;
ALTER TABLE events RENAME TO events_old;
ALTER TABLE merge_entries RENAME TO merge_entries_old;
CREATE TABLE fleets (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    name TEXT NOT NULL UNIQUE,
    description TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE TABLE vessels (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    fleet_id TEXT,
    name TEXT NOT NULL UNIQUE,
    repo_url TEXT NOT NULL,
    local_path TEXT,
    default_branch TEXT NOT NULL DEFAULT 'main',
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    working_directory TEXT,
    project_context TEXT,
    style_guide TEXT,
    landing_mode TEXT,
    branch_cleanup_policy TEXT,
    allow_concurrent_missions INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (fleet_id) REFERENCES fleets(id) ON DELETE SET NULL
);
CREATE TABLE captains (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    name TEXT NOT NULL UNIQUE,
    runtime TEXT NOT NULL DEFAULT 'ClaudeCode',
    state TEXT NOT NULL DEFAULT 'Idle',
    current_mission_id TEXT,
    current_dock_id TEXT,
    process_id INTEGER,
    recovery_attempts INTEGER NOT NULL DEFAULT 0,
    last_heartbeat_utc TEXT,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE TABLE voyages (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL DEFAULT 'Open',
    created_utc TEXT NOT NULL,
    completed_utc TEXT,
    last_update_utc TEXT NOT NULL,
    auto_push INTEGER,
    auto_create_pull_requests INTEGER,
    auto_merge_pull_requests INTEGER,
    landing_mode TEXT,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE TABLE missions (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    voyage_id TEXT,
    vessel_id TEXT,
    captain_id TEXT,
    title TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL DEFAULT 'Pending',
    priority INTEGER NOT NULL DEFAULT 100,
    parent_mission_id TEXT,
    branch_name TEXT,
    pr_url TEXT,
    created_utc TEXT NOT NULL,
    started_utc TEXT,
    completed_utc TEXT,
    last_update_utc TEXT NOT NULL,
    dock_id TEXT,
    process_id INTEGER,
    commit_hash TEXT,
    diff_snapshot TEXT,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (voyage_id) REFERENCES voyages(id) ON DELETE SET NULL,
    FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE SET NULL,
    FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL,
    FOREIGN KEY (parent_mission_id) REFERENCES missions(id) ON DELETE SET NULL
);
CREATE TABLE docks (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    vessel_id TEXT NOT NULL,
    captain_id TEXT,
    worktree_path TEXT,
    branch_name TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (vessel_id) REFERENCES vessels(id) ON DELETE CASCADE,
    FOREIGN KEY (captain_id) REFERENCES captains(id) ON DELETE SET NULL
);
CREATE TABLE signals (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    from_captain_id TEXT,
    to_captain_id TEXT,
    type TEXT NOT NULL DEFAULT 'Nudge',
    payload TEXT,
    read INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (from_captain_id) REFERENCES captains(id) ON DELETE SET NULL,
    FOREIGN KEY (to_captain_id) REFERENCES captains(id) ON DELETE SET NULL
);
CREATE TABLE events (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    event_type TEXT NOT NULL,
    entity_type TEXT,
    entity_id TEXT,
    captain_id TEXT,
    mission_id TEXT,
    vessel_id TEXT,
    voyage_id TEXT,
    message TEXT NOT NULL,
    payload TEXT,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE TABLE merge_entries (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    user_id TEXT,
    mission_id TEXT,
    vessel_id TEXT,
    branch_name TEXT NOT NULL,
    target_branch TEXT NOT NULL DEFAULT 'main',
    status TEXT NOT NULL DEFAULT 'Queued',
    priority INTEGER NOT NULL DEFAULT 0,
    batch_id TEXT,
    test_command TEXT,
    test_output TEXT,
    test_exit_code INTEGER,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    test_started_utc TEXT,
    completed_utc TEXT,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id),
    FOREIGN KEY (user_id) REFERENCES users(id)
);
INSERT INTO fleets (id, tenant_id, user_id, name, description, active, created_utc, last_update_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), name, description, active, created_utc, last_update_utc FROM fleets_old;
INSERT INTO captains (id, tenant_id, user_id, name, runtime, state, current_mission_id, current_dock_id, process_id, recovery_attempts, last_heartbeat_utc, created_utc, last_update_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), name, runtime, state, current_mission_id, current_dock_id, process_id, recovery_attempts, last_heartbeat_utc, created_utc, last_update_utc FROM captains_old;
INSERT INTO voyages (id, tenant_id, user_id, title, description, status, created_utc, completed_utc, last_update_utc, auto_push, auto_create_pull_requests, auto_merge_pull_requests, landing_mode)
SELECT id, tenant_id, COALESCE(user_id, 'default'), title, description, status, created_utc, completed_utc, last_update_utc, auto_push, auto_create_pull_requests, auto_merge_pull_requests, landing_mode FROM voyages_old;
INSERT INTO vessels (id, tenant_id, user_id, fleet_id, name, repo_url, local_path, default_branch, active, created_utc, last_update_utc, working_directory, project_context, style_guide, landing_mode, branch_cleanup_policy, allow_concurrent_missions)
SELECT id, tenant_id, COALESCE(user_id, 'default'), fleet_id, name, repo_url, local_path, default_branch, active, created_utc, last_update_utc, working_directory, project_context, style_guide, landing_mode, branch_cleanup_policy, allow_concurrent_missions FROM vessels_old;
INSERT INTO missions (id, tenant_id, user_id, voyage_id, vessel_id, captain_id, title, description, status, priority, parent_mission_id, branch_name, pr_url, created_utc, started_utc, completed_utc, last_update_utc, dock_id, process_id, commit_hash, diff_snapshot)
SELECT id, tenant_id, COALESCE(user_id, 'default'), voyage_id, vessel_id, captain_id, title, description, status, priority, parent_mission_id, branch_name, pr_url, created_utc, started_utc, completed_utc, last_update_utc, dock_id, process_id, commit_hash, diff_snapshot FROM missions_old;
INSERT INTO docks (id, tenant_id, user_id, vessel_id, captain_id, worktree_path, branch_name, active, created_utc, last_update_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), vessel_id, captain_id, worktree_path, branch_name, active, created_utc, last_update_utc FROM docks_old;
INSERT INTO signals (id, tenant_id, user_id, from_captain_id, to_captain_id, type, payload, read, created_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), from_captain_id, to_captain_id, type, payload, read, created_utc FROM signals_old;
INSERT INTO events (id, tenant_id, user_id, event_type, entity_type, entity_id, captain_id, mission_id, vessel_id, voyage_id, message, payload, created_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), event_type, entity_type, entity_id, captain_id, mission_id, vessel_id, voyage_id, message, payload, created_utc FROM events_old;
INSERT INTO merge_entries (id, tenant_id, user_id, mission_id, vessel_id, branch_name, target_branch, status, priority, batch_id, test_command, test_output, test_exit_code, created_utc, last_update_utc, test_started_utc, completed_utc)
SELECT id, tenant_id, COALESCE(user_id, 'default'), mission_id, vessel_id, branch_name, target_branch, status, priority, batch_id, test_command, test_output, test_exit_code, created_utc, last_update_utc, test_started_utc, completed_utc FROM merge_entries_old;
DROP TABLE fleets_old;
DROP TABLE vessels_old;
DROP TABLE captains_old;
DROP TABLE voyages_old;
DROP TABLE missions_old;
DROP TABLE docks_old;
DROP TABLE signals_old;
DROP TABLE events_old;
DROP TABLE merge_entries_old;
'@; Set-Content -Path '%FK_SQL_FILE%' -Value $sql -Encoding UTF8"
sqlite3 "!DB_PATH!" < "!FK_SQL_FILE!"
del /q "!FK_SQL_FILE!" >nul 2>nul

echo Operational foreign keys added.

REM Create tenant indexes on operational tables
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_tenant ON fleets(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_tenant_name ON fleets(tenant_id, name);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_created_utc ON fleets(created_utc);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_tenant ON vessels(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_tenant_name ON vessels(tenant_id, name);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_created_utc ON vessels(created_utc);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_tenant ON captains(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_tenant_state ON captains(tenant_id, state);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_created_utc ON captains(created_utc);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant ON missions(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_status ON missions(tenant_id, status);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_captain ON missions(tenant_id, captain_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_voyages_tenant ON voyages(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_voyages_tenant_status ON voyages(tenant_id, status);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_voyages_created_utc ON voyages(created_utc);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_tenant ON docks(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_tenant_captain ON docks(tenant_id, captain_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_created_utc ON docks(created_utc);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_tenant ON signals(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, read);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant ON events(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_type ON events(tenant_id, event_type);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_vessel ON events(tenant_id, vessel_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_voyage ON events(tenant_id, voyage_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_captain ON events(tenant_id, captain_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_mission ON events(tenant_id, mission_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_created ON events(tenant_id, created_utc DESC);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant ON merge_entries(tenant_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_user ON fleets(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_tenant_user ON fleets(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_user ON vessels(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_tenant_user ON vessels(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_user ON captains(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_tenant_user ON captains(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_voyages_user ON voyages(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_voyages_tenant_user ON voyages(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_user ON missions(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_tenant_user ON missions(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_user ON docks(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_docks_tenant_user ON docks(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_user ON signals(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_signals_tenant_user ON signals(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_user ON events(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_events_tenant_user ON events(tenant_id, user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_user ON merge_entries(user_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_user ON merge_entries(tenant_id, user_id);"

echo Indexes created.

REM Record schema migration so the server does not try to apply it again
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_utc TEXT NOT NULL);"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (13, 'Multi-tenant: add tenants, users, credentials tables and tenant_id columns', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (14, 'Protected resources and user ownership', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (15, 'Add tenant and user foreign keys to operational tables', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (16, 'Add tenant admin role to users', datetime('now'));"

echo Schema version recorded.

REM Print summary
echo.
echo Database migration complete!

REM ========================================
REM Part 2: Settings Migration
REM ========================================
echo.
echo --- Settings Migration ---
echo.

set "SETTINGS_BACKUP=%SETTINGS_FILE%.v0.2.0.bak"
copy /y "%SETTINGS_FILE%" "%SETTINGS_BACKUP%" >nul
echo Settings backup created: %SETTINGS_BACKUP%

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$settingsPath = '%SETTINGS_FILE%';" ^
    "$json = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json;" ^
    "" ^
    "if (-not ($json.PSObject.Properties.Name -contains 'allowSelfRegistration')) {" ^
    "    $json | Add-Member -NotePropertyName 'allowSelfRegistration' -NotePropertyValue $true;" ^
    "}" ^
    "" ^
    "if (-not ($json.PSObject.Properties.Name -contains 'requireAuthForShutdown')) {" ^
    "    $json | Add-Member -NotePropertyName 'requireAuthForShutdown' -NotePropertyValue $false;" ^
    "}" ^
    "" ^
    "$json | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsPath -Encoding UTF8;" ^
    "" ^
    "Write-Host 'Settings updated.';" ^
    "Write-Host '';" ^
    "Write-Host 'Changes:';" ^
    "Write-Host '  - Added allowSelfRegistration: true (if not present)';" ^
    "Write-Host '  - Added requireAuthForShutdown: false (if not present)';"

REM ========================================
REM Done
REM ========================================
echo.
echo ====================================
echo Migration v0.2.0 -^> v0.3.0 complete!
echo ====================================
echo.
echo Default login credentials:
echo   Email:    admin@armada
echo   Password: password
echo   Bearer:   Authorization: Bearer default
echo.
echo IMPORTANT: Change the default password after first login.
echo.

exit /b 0
