#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: Add Pipelines Tables"
echo "======================================="
echo ""
echo "This script adds the pipelines and pipeline_stages tables, plus default_pipeline_id"
echo "columns to the fleets and vessels tables."
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
DB_BACKUP="${DB_PATH}.pre-pipelines.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# Check if table already exists
EXISTING=$(sqlite3 "$DB_PATH" "SELECT name FROM sqlite_master WHERE type='table' AND name='pipelines';" | grep -c "pipelines" || true)
if [ "$EXISTING" -gt 0 ]; then
    echo ""
    echo "Table already exists. Migration has already been applied."
    exit 0
fi

echo ""
echo "Creating pipelines and pipeline_stages tables..."

sqlite3 "$DB_PATH" <<'MIGRATION_SQL'
CREATE TABLE pipelines (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    name TEXT NOT NULL,
    description TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE pipeline_stages (
    id TEXT PRIMARY KEY,
    pipeline_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    stage_order INTEGER NOT NULL,
    persona_name TEXT,
    auto_advance INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (pipeline_id) REFERENCES pipelines(id)
);

CREATE UNIQUE INDEX idx_pipelines_tenant_name ON pipelines(tenant_id, name);
CREATE INDEX idx_pipelines_active ON pipelines(active);
CREATE UNIQUE INDEX idx_pipeline_stages_pipeline_order ON pipeline_stages(pipeline_id, stage_order);
CREATE INDEX idx_pipeline_stages_pipeline_id ON pipeline_stages(pipeline_id);
CREATE INDEX idx_pipeline_stages_persona ON pipeline_stages(persona_name);

ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;
ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;

CREATE INDEX idx_fleets_default_pipeline ON fleets(default_pipeline_id);
CREATE INDEX idx_vessels_default_pipeline ON vessels(default_pipeline_id);
MIGRATION_SQL

echo "Migration complete."
echo ""
echo "New tables created: pipelines, pipeline_stages"
echo "New columns added: fleets.default_pipeline_id, vessels.default_pipeline_id"
echo "  Indexes: idx_pipelines_tenant_name (unique), idx_pipelines_active,"
echo "           idx_pipeline_stages_pipeline_order (unique), idx_pipeline_stages_pipeline_id,"
echo "           idx_pipeline_stages_persona, idx_fleets_default_pipeline,"
echo "           idx_vessels_default_pipeline"
echo ""
