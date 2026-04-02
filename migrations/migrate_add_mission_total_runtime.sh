#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Mission Total Runtime"
echo "============================================"
echo ""
echo "This script adds total_runtime_ms column to the missions table."
echo "This field stores the total mission runtime in milliseconds,"
echo "typically computed from CompletedUtc minus StartedUtc."
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

# Check if column already exists
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -c "total_runtime_ms" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Column already exists. Migration has already been applied."
    exit 0
fi

# Create backup
DB_BACKUP="${DB_PATH}.pre-mission-total-runtime.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

echo ""
echo "Adding total_runtime_ms column to missions table..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
ALTER TABLE missions ADD COLUMN total_runtime_ms INTEGER;
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New column added to missions table:"
echo "  - total_runtime_ms (INTEGER, nullable)"
echo ""
echo "This column is populated when mission runtime is computed and persisted."
