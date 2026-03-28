#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Personas Table"
echo "====================================="
echo ""
echo "This script adds the personas table for named agent roles."
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
DB_BACKUP="${DB_PATH}.pre-personas.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# Check if table already exists
EXISTING=$(sqlite3 "$DB_PATH" "SELECT name FROM sqlite_master WHERE type='table' AND name='personas';" | grep -c "personas" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Table already exists. Migration has already been applied."
    exit 0
fi

echo ""
echo "Creating personas table..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
CREATE TABLE personas (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    name TEXT NOT NULL,
    description TEXT,
    prompt_template_name TEXT NOT NULL,
    is_built_in INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id, name);
CREATE INDEX idx_personas_active ON personas(active);
CREATE INDEX idx_personas_prompt_template ON personas(prompt_template_name);
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New table created: personas"
echo "  Indexes: idx_personas_tenant_name (unique), idx_personas_active, idx_personas_prompt_template"
echo ""
