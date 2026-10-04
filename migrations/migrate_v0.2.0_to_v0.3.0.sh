#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: v0.2.0 to v0.3.0"
echo "===================================="
echo ""
echo "This script performs two migrations:"
echo "  1. Database: creates auth tables, backfills tenant_id on all records"
echo "  2. Settings: adds new v0.3.0 settings with defaults"
echo ""

# Check for sqlite3
if ! command -v sqlite3 &> /dev/null; then
    echo "ERROR: sqlite3 is required but not installed."
    echo ""
    echo "Install sqlite3 using your package manager:"
    echo "  Ubuntu/Debian: sudo apt install sqlite3"
    echo "  macOS:         brew install sqlite3"
    echo "  Fedora/RHEL:   sudo dnf install sqlite3"
    echo "  Arch:          sudo pacman -S sqlite"
    exit 1
fi

# Check for jq
if ! command -v jq &> /dev/null; then
    echo "ERROR: jq is required but not installed."
    echo ""
    echo "Install jq using your package manager:"
    echo "  Ubuntu/Debian: sudo apt install jq"
    echo "  macOS:         brew install jq"
    echo "  Fedora/RHEL:   sudo dnf install jq"
    echo "  Arch:          sudo pacman -S jq"
    exit 1
fi

# Determine paths
SETTINGS_FILE="${1:-$HOME/.armada/settings.json}"

echo "Settings file: $SETTINGS_FILE"

if [ ! -f "$SETTINGS_FILE" ]; then
    echo ""
    echo "ERROR: Settings file not found: $SETTINGS_FILE"
    echo ""
    echo "Usage: $0 [path/to/settings.json]"
    echo "Default path: ~/.armada/settings.json"
    exit 1
fi

# Validate JSON
if ! jq empty "$SETTINGS_FILE" 2>/dev/null; then
    echo "ERROR: Failed to parse settings.json. The file may contain invalid JSON."
    exit 1
fi

# Determine database path from settings
DB_PATH=$(jq -r '.database.filename // .databasePath // empty' "$SETTINGS_FILE")
if [ -z "$DB_PATH" ]; then
    # Fallback: default location
    DB_PATH="$HOME/.armada/armada.db"
fi

echo "Database file: $DB_PATH"

if [ ! -f "$DB_PATH" ]; then
    echo ""
    echo "ERROR: Database file not found: $DB_PATH"
    echo "Make sure the Armada server has been run at least once to create the database."
    exit 1
fi

# ========================================
# Part 1: Database Migration
# ========================================
echo ""
echo "--- Database Migration ---"
echo ""

# Create backup
DB_BACKUP="${DB_PATH}.v0.2.0.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# SHA256 of "password"
PASSWORD_HASH="5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8"

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
-- Create auth tables
CREATE TABLE IF NOT EXISTS tenants (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    email TEXT NOT NULL,
    password_sha256 TEXT NOT NULL,
    first_name TEXT,
    last_name TEXT,
    is_admin INTEGER NOT NULL DEFAULT 0,
    is_tenant_admin INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS credentials (
    id TEXT PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    name TEXT,
    bearer_token TEXT NOT NULL UNIQUE,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- Indexes on auth tables
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenant_email ON users(tenant_id, email);
CREATE INDEX IF NOT EXISTS idx_users_tenant ON users(tenant_id);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_credentials_tenant ON credentials(tenant_id);
CREATE INDEX IF NOT EXISTS idx_credentials_user ON credentials(user_id);
CREATE INDEX IF NOT EXISTS idx_credentials_bearer ON credentials(bearer_token);
CREATE INDEX IF NOT EXISTS idx_tenants_active ON tenants(active);
CREATE INDEX IF NOT EXISTS idx_credentials_tenant_user ON credentials(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_credentials_active ON credentials(active);

-- Seed default tenant
INSERT OR IGNORE INTO tenants (id, name, active, created_utc, last_update_utc)
    VALUES ('default', 'Default Tenant', 1, datetime('now'), datetime('now'));

-- Seed default admin user (admin@armada / password)
INSERT OR IGNORE INTO users (id, tenant_id, email, password_sha256, is_admin, is_tenant_admin, active, created_utc, last_update_utc)
    VALUES ('default', 'default', 'admin@armada',
            '5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8',
            1, 1, 1, datetime('now'), datetime('now'));

-- Seed default credential (bearer token: "default")
INSERT OR IGNORE INTO credentials (id, tenant_id, user_id, bearer_token, active, created_utc, last_update_utc)
    VALUES ('default', 'default', 'default', 'default', 1, datetime('now'), datetime('now'));
MIGRATION_SQL

echo "Auth tables created and seeded."

# Add tenant_id columns (ignore errors if they already exist)
for TABLE in fleets vessels captains voyages missions docks signals events merge_entries; do
    sqlite3 "$DB_PATH" "ALTER TABLE $TABLE ADD COLUMN tenant_id TEXT;" 2>/dev/null || true
done
for TABLE in fleets vessels captains voyages missions docks signals events merge_entries; do
    sqlite3 "$DB_PATH" "ALTER TABLE $TABLE ADD COLUMN user_id TEXT;" 2>/dev/null || true
done

echo "tenant_id columns added to operational tables."

# Backfill all rows with default tenant
sqlite3 "$DB_PATH" <<'BACKFILL_SQL'
UPDATE fleets SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE vessels SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE captains SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE voyages SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE missions SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE docks SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE signals SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE events SET tenant_id = 'default' WHERE tenant_id IS NULL;
UPDATE merge_entries SET tenant_id = 'default' WHERE tenant_id IS NULL;
ALTER TABLE tenants ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN is_tenant_admin INTEGER NOT NULL DEFAULT 0;
ALTER TABLE users ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;
ALTER TABLE credentials ADD COLUMN is_protected INTEGER NOT NULL DEFAULT 0;
UPDATE users SET is_tenant_admin = 1 WHERE is_admin = 1;
UPDATE tenants SET is_protected = 1 WHERE id IN ('default', 'ten_system');
UPDATE users SET is_protected = 1 WHERE id IN ('default', 'usr_system');
UPDATE credentials SET is_protected = 1 WHERE user_id IN ('default', 'usr_system');
UPDATE fleets SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = fleets.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE vessels SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = vessels.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE captains SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = captains.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE voyages SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = voyages.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE missions SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = missions.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE docks SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = docks.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE signals SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = signals.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE events SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = events.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
UPDATE merge_entries SET user_id = COALESCE((SELECT id FROM users u WHERE u.tenant_id = merge_entries.tenant_id ORDER BY u.created_utc LIMIT 1), 'default') WHERE user_id IS NULL;
BACKFILL_SQL

echo "All existing records backfilled with tenant_id = 'default'."

sqlite3 "$DB_PATH" <<'FK_SQL'
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
FK_SQL

echo "Operational foreign keys added."

# Create tenant indexes on operational tables
sqlite3 "$DB_PATH" <<'INDEX_SQL'
CREATE INDEX IF NOT EXISTS idx_fleets_tenant ON fleets(tenant_id);
CREATE INDEX IF NOT EXISTS idx_fleets_tenant_name ON fleets(tenant_id, name);
CREATE INDEX IF NOT EXISTS idx_fleets_created_utc ON fleets(created_utc);
CREATE INDEX IF NOT EXISTS idx_vessels_tenant ON vessels(tenant_id);
CREATE INDEX IF NOT EXISTS idx_vessels_tenant_fleet ON vessels(tenant_id, fleet_id);
CREATE INDEX IF NOT EXISTS idx_vessels_tenant_name ON vessels(tenant_id, name);
CREATE INDEX IF NOT EXISTS idx_vessels_created_utc ON vessels(created_utc);
CREATE INDEX IF NOT EXISTS idx_captains_tenant ON captains(tenant_id);
CREATE INDEX IF NOT EXISTS idx_captains_tenant_state ON captains(tenant_id, state);
CREATE INDEX IF NOT EXISTS idx_captains_created_utc ON captains(created_utc);
CREATE INDEX IF NOT EXISTS idx_missions_tenant ON missions(tenant_id);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_status ON missions(tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_vessel ON missions(tenant_id, vessel_id);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_voyage ON missions(tenant_id, voyage_id);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_captain ON missions(tenant_id, captain_id);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_status_priority ON missions(tenant_id, status, priority ASC, created_utc ASC);
CREATE INDEX IF NOT EXISTS idx_voyages_tenant ON voyages(tenant_id);
CREATE INDEX IF NOT EXISTS idx_voyages_tenant_status ON voyages(tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_voyages_created_utc ON voyages(created_utc);
CREATE INDEX IF NOT EXISTS idx_docks_tenant ON docks(tenant_id);
CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel ON docks(tenant_id, vessel_id);
CREATE INDEX IF NOT EXISTS idx_docks_tenant_vessel_available ON docks(tenant_id, vessel_id, active, captain_id);
CREATE INDEX IF NOT EXISTS idx_docks_tenant_captain ON docks(tenant_id, captain_id);
CREATE INDEX IF NOT EXISTS idx_docks_created_utc ON docks(created_utc);
CREATE INDEX IF NOT EXISTS idx_signals_tenant ON signals(tenant_id);
CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain ON signals(tenant_id, to_captain_id);
CREATE INDEX IF NOT EXISTS idx_signals_tenant_to_captain_read ON signals(tenant_id, to_captain_id, read);
CREATE INDEX IF NOT EXISTS idx_signals_tenant_created ON signals(tenant_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS idx_events_tenant ON events(tenant_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_type ON events(tenant_id, event_type);
CREATE INDEX IF NOT EXISTS idx_events_tenant_entity ON events(tenant_id, entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_vessel ON events(tenant_id, vessel_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_voyage ON events(tenant_id, voyage_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_captain ON events(tenant_id, captain_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_mission ON events(tenant_id, mission_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_created ON events(tenant_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant ON merge_entries(tenant_id);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status ON merge_entries(tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_status_priority ON merge_entries(tenant_id, status, priority ASC, created_utc ASC);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_vessel ON merge_entries(tenant_id, vessel_id);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_mission ON merge_entries(tenant_id, mission_id);
CREATE INDEX IF NOT EXISTS idx_fleets_user ON fleets(user_id);
CREATE INDEX IF NOT EXISTS idx_fleets_tenant_user ON fleets(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_vessels_user ON vessels(user_id);
CREATE INDEX IF NOT EXISTS idx_vessels_tenant_user ON vessels(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_captains_user ON captains(user_id);
CREATE INDEX IF NOT EXISTS idx_captains_tenant_user ON captains(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_voyages_user ON voyages(user_id);
CREATE INDEX IF NOT EXISTS idx_voyages_tenant_user ON voyages(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_missions_user ON missions(user_id);
CREATE INDEX IF NOT EXISTS idx_missions_tenant_user ON missions(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_docks_user ON docks(user_id);
CREATE INDEX IF NOT EXISTS idx_docks_tenant_user ON docks(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_signals_user ON signals(user_id);
CREATE INDEX IF NOT EXISTS idx_signals_tenant_user ON signals(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_events_user ON events(user_id);
CREATE INDEX IF NOT EXISTS idx_events_tenant_user ON events(tenant_id, user_id);
CREATE INDEX IF NOT EXISTS idx_merge_entries_user ON merge_entries(user_id);
CREATE INDEX IF NOT EXISTS idx_merge_entries_tenant_user ON merge_entries(tenant_id, user_id);
INDEX_SQL

echo "Indexes created."

sqlite3 "$DB_PATH" <<'SCHEMA_SQL'
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (13, 'Multi-tenant: add tenants, users, credentials tables and tenant_id columns', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (14, 'Protected resources and user ownership', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (15, 'Add tenant and user foreign keys to operational tables', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (16, 'Add tenant admin role to users', datetime('now'));
SCHEMA_SQL

echo "Schema version recorded."

# Print summary
TENANT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM tenants;")
USER_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM users;")
FLEET_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM fleets WHERE tenant_id = 'default';")
VESSEL_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM vessels WHERE tenant_id = 'default';")
MISSION_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM missions WHERE tenant_id = 'default';")
VOYAGE_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM voyages WHERE tenant_id = 'default';")

echo ""
echo "Database migration complete!"
echo "  Tenants: $TENANT_COUNT"
echo "  Users: $USER_COUNT"
echo "  Backfilled: $FLEET_COUNT fleets, $VESSEL_COUNT vessels, $MISSION_COUNT missions, $VOYAGE_COUNT voyages"

# ========================================
# Part 2: Settings Migration
# ========================================
echo ""
echo "--- Settings Migration ---"
echo ""

SETTINGS_BACKUP="${SETTINGS_FILE}.v0.2.0.bak"
cp "$SETTINGS_FILE" "$SETTINGS_BACKUP"
echo "Settings backup created: $SETTINGS_BACKUP"

# Add new v0.3.0 settings with defaults (only if not already present)
jq '
    if has("allowSelfRegistration") then . else . + {"allowSelfRegistration": true} end |
    if has("requireAuthForShutdown") then . else . + {"requireAuthForShutdown": false} end
' "$SETTINGS_FILE" > "${SETTINGS_FILE}.tmp" && mv "${SETTINGS_FILE}.tmp" "$SETTINGS_FILE"

echo "Settings updated."
echo ""
echo "Changes:"
echo "  - Added 'allowSelfRegistration': true (if not present)"
echo "  - Added 'requireAuthForShutdown': false (if not present)"

# ========================================
# Done
# ========================================
echo ""
echo "===================================="
echo "Migration v0.2.0 -> v0.3.0 complete!"
echo "===================================="
echo ""
echo "Default login credentials:"
echo "  Email:    admin@armada"
echo "  Password: password"
echo "  Bearer:   Authorization: Bearer default"
echo ""
echo "IMPORTANT: Change the default password after first login."
echo ""
