#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Model Context Fields"
echo "============================================"
echo ""
echo "This script adds EnableModelContext and ModelContext columns to the vessels table."
echo "These fields enable AI agents to accumulate knowledge about repositories across missions."
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
DB_BACKUP="${DB_PATH}.pre-model-context.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# Check if columns already exist
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(vessels);" | grep -c "enable_model_context" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Columns already exist. Migration has already been applied."
    exit 0
fi

echo ""
echo "Adding enable_model_context and model_context columns to vessels table..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
ALTER TABLE vessels ADD COLUMN enable_model_context INTEGER NOT NULL DEFAULT 1;
ALTER TABLE vessels ADD COLUMN model_context TEXT;
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New columns added to vessels table:"
echo "  - enable_model_context (INTEGER, default 0)"
echo "  - model_context (TEXT, nullable)"
echo ""
echo "To enable model context for a vessel, set enable_model_context to 1"
echo "via the dashboard, REST API, or MCP tools."
