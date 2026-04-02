#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: v0.4.0 to v0.5.0"
echo "===================================="
echo ""
echo "This script prepares the schema changes for v0.5.0:"
echo "  1. Adds captains.model"
echo "  2. Adds missions.total_runtime_ms"
echo ""
echo "SQLite is applied automatically from the local Armada settings file."
echo "SQL for PostgreSQL, MySQL, and SQL Server is included below for manual execution."
echo ""

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

if command -v jq &> /dev/null; then
    DB_PATH=$(jq -r '.database.filename // .databasePath // empty' "$SETTINGS_FILE")
fi

if [ -z "${DB_PATH:-}" ]; then
    DB_PATH="$HOME/.armada/armada.db"
fi

echo "Database file: $DB_PATH"

if [ ! -f "$DB_PATH" ]; then
    echo ""
    echo "ERROR: Database file not found: $DB_PATH"
    echo "Make sure the Armada server has been run at least once to create the database."
    exit 1
fi

DB_BACKUP="${DB_PATH}.pre-v0.5.0.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

echo ""
echo "--- SQLite ---"

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "model" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE captains ADD COLUMN model TEXT;"
    echo "  Added captains.model"
else
    echo "  captains.model already exists"
fi

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -c "total_runtime_ms" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE missions ADD COLUMN total_runtime_ms INTEGER;"
    echo "  Added missions.total_runtime_ms"
else
    echo "  missions.total_runtime_ms already exists"
fi

sqlite3 "$DB_PATH" <<'SQL'
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (26, 'Add model to captains', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (27, 'Add total_runtime_ms to missions', datetime('now'));
SQL

echo "  Schema versions 26-27 recorded."

echo ""
echo "--- PostgreSQL SQL ---"
cat <<'SQL'
ALTER TABLE captains ADD COLUMN model TEXT;
ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT;
INSERT INTO schema_migrations (version, description, applied_utc)
VALUES (26, 'Add model to captains', NOW())
ON CONFLICT (version) DO NOTHING;
INSERT INTO schema_migrations (version, description, applied_utc)
VALUES (27, 'Add total_runtime_ms to missions', NOW())
ON CONFLICT (version) DO NOTHING;
SQL

echo ""
echo "--- MySQL SQL ---"
cat <<'SQL'
ALTER TABLE captains ADD COLUMN model TEXT;
ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT;
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INT NOT NULL PRIMARY KEY,
    description LONGTEXT NOT NULL,
    applied_utc DATETIME(6) NOT NULL
);
INSERT IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (26, 'Add model to captains', UTC_TIMESTAMP(6));
INSERT IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (27, 'Add total_runtime_ms to missions', UTC_TIMESTAMP(6));
SQL

echo ""
echo "--- SQL Server SQL ---"
cat <<'SQL'
ALTER TABLE captains ADD model NVARCHAR(MAX) NULL;
ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[schema_migrations]') AND type = N'U')
BEGIN
    CREATE TABLE schema_migrations (
        version INT NOT NULL PRIMARY KEY,
        description NVARCHAR(MAX) NOT NULL,
        applied_utc DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 26)
BEGIN
    INSERT INTO schema_migrations (version, description, applied_utc)
    VALUES (26, 'Add model to captains', SYSUTCDATETIME());
END;
IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 27)
BEGIN
    INSERT INTO schema_migrations (version, description, applied_utc)
    VALUES (27, 'Add total_runtime_ms to missions', SYSUTCDATETIME());
END;
SQL

echo ""
echo "===================================="
echo "Migration v0.4.0 -> v0.5.0 complete!"
echo "===================================="
echo ""
echo "Summary of changes:"
echo "  SQLite: applied captains.model and missions.total_runtime_ms"
echo "  PostgreSQL: SQL block emitted above"
echo "  MySQL: SQL block emitted above"
echo "  SQL Server: SQL block emitted above"
