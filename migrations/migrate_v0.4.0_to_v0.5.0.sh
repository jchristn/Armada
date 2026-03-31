#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: v0.4.0 to v0.5.0"
echo "===================================="
echo ""
echo "This script performs the following database migrations:"
echo "  1. Adds model column to captains (AI model selection)"
echo "  2. Adds total_runtime_seconds column to missions"
echo ""
echo "All operations are idempotent and safe to run multiple times."
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

# Check for jq
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

# Create backup
DB_BACKUP="${DB_PATH}.pre-v0.5.0.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# ========================================
# 1. Add model column to captains
# ========================================
echo ""
echo "--- 1. Captain Model ---"

# Check if column already exists
if sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -q "model"; then
    echo "  model column already exists: SKIP"
else
    sqlite3 "$DB_PATH" "ALTER TABLE captains ADD COLUMN model TEXT;"
    echo "  model column added: OK"
fi

# ========================================
# 2. Add total_runtime_seconds to missions
# ========================================
echo ""
echo "--- 2. Mission Total Runtime ---"

if sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -q "total_runtime_seconds"; then
    echo "  total_runtime_seconds column already exists: SKIP"
else
    sqlite3 "$DB_PATH" "ALTER TABLE missions ADD COLUMN total_runtime_seconds REAL;"
    echo "  total_runtime_seconds column added: OK"
fi

# ========================================
# Record schema versions
# ========================================
echo ""
echo "--- Recording schema versions ---"

sqlite3 "$DB_PATH" <<'SQL'
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (26, 'Add model to captains for specifying AI model', datetime('now'));

INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (27, 'Add total_runtime_seconds to missions', datetime('now'));
SQL

echo "  Schema versions 26-27 recorded: OK"

echo ""
echo "===================================="
echo "Migration complete! v0.4.0 -> v0.5.0"
echo "===================================="
echo ""
echo "Backup file: $DB_BACKUP"
echo "You can remove the backup once you verify everything works."
