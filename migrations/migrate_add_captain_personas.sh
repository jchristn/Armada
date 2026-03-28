#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Captain Persona Fields"
echo "============================================="
echo ""
echo "This script adds allowed_personas and preferred_persona columns to the captains table."
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
DB_BACKUP="${DB_PATH}.pre-captain-personas.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# Check if columns already exist
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "allowed_personas" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Columns already exist. Migration has already been applied."
    exit 0
fi

echo ""
echo "Adding allowed_personas and preferred_persona columns to captains table..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
ALTER TABLE captains ADD COLUMN allowed_personas TEXT;
ALTER TABLE captains ADD COLUMN preferred_persona TEXT;

CREATE INDEX idx_captains_preferred_persona ON captains(preferred_persona);
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New columns added to captains table:"
echo "  - allowed_personas (TEXT, nullable, JSON array of persona names)"
echo "  - preferred_persona (TEXT, nullable)"
echo "  Index: idx_captains_preferred_persona"
echo ""
