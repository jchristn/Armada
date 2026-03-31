#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: v0.4.0 to v0.5.0"
echo "===================================="
echo ""
echo "This script adds:"
echo "  - model column to captains (per-captain AI model selection)"
echo "  - total_runtime_seconds column to missions (wall-clock runtime tracking)"
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

echo ""

# Add model column to captains
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "model" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo "Captain model column already exists, skipping."
else
    echo "Adding model column to captains table..."
    sqlite3 "$DB_PATH" "ALTER TABLE captains ADD COLUMN model TEXT;"
    echo "  Done."
fi

# Add total_runtime_seconds column to missions
EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -c "total_runtime_seconds" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo "Mission total_runtime_seconds column already exists, skipping."
else
    echo "Adding total_runtime_seconds column to missions table..."
    sqlite3 "$DB_PATH" "ALTER TABLE missions ADD COLUMN total_runtime_seconds REAL;"
    echo "  Done."
fi

echo ""
echo "Migration complete."
echo ""
echo "New columns added:"
echo "  - captains.model (TEXT, nullable) - AI model override per captain"
echo "  - missions.total_runtime_seconds (REAL, nullable) - wall-clock runtime"
echo ""
