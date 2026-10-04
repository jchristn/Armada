#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Captain System Instructions"
echo "=================================================="
echo ""
echo "This script adds SystemInstructions column to the captains table."
echo "This field stores per-captain user-supplied instructions that are"
echo "injected into every mission prompt to specialize captain behavior."
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
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "system_instructions" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Column already exists. Migration has already been applied."
    exit 0
fi

# Create backup
DB_BACKUP="${DB_PATH}.pre-captain-instructions.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

echo ""
echo "Adding system_instructions column to captains table..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
ALTER TABLE captains ADD COLUMN system_instructions TEXT;
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New column added to captains table:"
echo "  - system_instructions (TEXT, nullable)"
echo ""
echo "Set system instructions via the dashboard, REST API, or MCP tools"
echo "to specialize captain behavior during missions."
