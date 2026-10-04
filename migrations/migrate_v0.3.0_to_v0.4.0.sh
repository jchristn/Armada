#!/usr/bin/env bash
set -euo pipefail

echo ""
echo "Armada Migration: v0.3.0 to v0.4.0"
echo "===================================="
echo ""
echo "This script performs the following database migrations:"
echo "  1. Creates prompt_templates table + indexes"
echo "  2. Creates personas table + indexes"
echo "  3. Creates pipelines and pipeline_stages tables + indexes"
echo "  4. Adds allowed_personas and preferred_persona columns to captains"
echo "  5. Adds persona and depends_on_mission_id columns to missions"
echo "  6. Adds default_pipeline_id column to fleets and vessels"
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
DB_BACKUP="${DB_PATH}.pre-v0.4.0.bak"
cp "$DB_PATH" "$DB_BACKUP"
echo "Database backup created: $DB_BACKUP"

# ========================================
# 1. prompt_templates table
# ========================================
echo ""
echo "--- 1. Prompt Templates ---"

sqlite3 "$DB_PATH" <<'SQL'
CREATE TABLE IF NOT EXISTS prompt_templates (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    name TEXT NOT NULL,
    description TEXT,
    category TEXT NOT NULL DEFAULT 'mission',
    content TEXT NOT NULL,
    is_built_in INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);
CREATE INDEX IF NOT EXISTS idx_prompt_templates_category ON prompt_templates(category);
CREATE INDEX IF NOT EXISTS idx_prompt_templates_active ON prompt_templates(active);
SQL

echo "  prompt_templates table: OK"

# ========================================
# 2. personas table
# ========================================
echo ""
echo "--- 2. Personas ---"

sqlite3 "$DB_PATH" <<'SQL'
CREATE TABLE IF NOT EXISTS personas (
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

CREATE UNIQUE INDEX IF NOT EXISTS idx_personas_tenant_name ON personas(tenant_id, name);
CREATE INDEX IF NOT EXISTS idx_personas_active ON personas(active);
CREATE INDEX IF NOT EXISTS idx_personas_prompt_template ON personas(prompt_template_name);
SQL

echo "  personas table: OK"

# ========================================
# 3. pipelines and pipeline_stages tables
# ========================================
echo ""
echo "--- 3. Pipelines ---"

sqlite3 "$DB_PATH" <<'SQL'
CREATE TABLE IF NOT EXISTS pipelines (
    id TEXT PRIMARY KEY,
    tenant_id TEXT,
    name TEXT NOT NULL,
    description TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_update_utc TEXT NOT NULL,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE TABLE IF NOT EXISTS pipeline_stages (
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

CREATE UNIQUE INDEX IF NOT EXISTS idx_pipelines_tenant_name ON pipelines(tenant_id, name);
CREATE INDEX IF NOT EXISTS idx_pipelines_active ON pipelines(active);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pipeline_stages_pipeline_order ON pipeline_stages(pipeline_id, stage_order);
CREATE INDEX IF NOT EXISTS idx_pipeline_stages_pipeline_id ON pipeline_stages(pipeline_id);
CREATE INDEX IF NOT EXISTS idx_pipeline_stages_persona ON pipeline_stages(persona_name);
SQL

echo "  pipelines table: OK"
echo "  pipeline_stages table: OK"

# ========================================
# 4. captains: add persona columns
# ========================================
echo ""
echo "--- 4. Captain Persona Columns ---"

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "allowed_personas" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE captains ADD COLUMN allowed_personas TEXT;"
    echo "  Added captains.allowed_personas"
else
    echo "  captains.allowed_personas already exists"
fi

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(captains);" | grep -c "preferred_persona" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE captains ADD COLUMN preferred_persona TEXT;"
    echo "  Added captains.preferred_persona"
else
    echo "  captains.preferred_persona already exists"
fi

sqlite3 "$DB_PATH" "CREATE INDEX IF NOT EXISTS idx_captains_preferred_persona ON captains(preferred_persona);"
echo "  Index idx_captains_preferred_persona: OK"

# ========================================
# 5. missions: add persona and dependency columns
# ========================================
echo ""
echo "--- 5. Mission Persona and Dependency Columns ---"

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -c "persona" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE missions ADD COLUMN persona TEXT;"
    echo "  Added missions.persona"
else
    echo "  missions.persona already exists"
fi

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(missions);" | grep -c "depends_on_mission_id" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE missions ADD COLUMN depends_on_mission_id TEXT;"
    echo "  Added missions.depends_on_mission_id"
else
    echo "  missions.depends_on_mission_id already exists"
fi

sqlite3 "$DB_PATH" "CREATE INDEX IF NOT EXISTS idx_missions_persona ON missions(persona);"
sqlite3 "$DB_PATH" "CREATE INDEX IF NOT EXISTS idx_missions_depends_on ON missions(depends_on_mission_id);"
echo "  Index idx_missions_persona: OK"
echo "  Index idx_missions_depends_on: OK"

# ========================================
# 6. fleets and vessels: add default_pipeline_id
# ========================================
echo ""
echo "--- 6. Default Pipeline Columns ---"

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(fleets);" | grep -c "default_pipeline_id" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;"
    echo "  Added fleets.default_pipeline_id"
else
    echo "  fleets.default_pipeline_id already exists"
fi

EXISTING=$(sqlite3 "$DB_PATH" "PRAGMA table_info(vessels);" | grep -c "default_pipeline_id" || true)
if [ "$EXISTING" -eq 0 ]; then
    sqlite3 "$DB_PATH" "ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;"
    echo "  Added vessels.default_pipeline_id"
else
    echo "  vessels.default_pipeline_id already exists"
fi

sqlite3 "$DB_PATH" "CREATE INDEX IF NOT EXISTS idx_fleets_default_pipeline ON fleets(default_pipeline_id);"
sqlite3 "$DB_PATH" "CREATE INDEX IF NOT EXISTS idx_vessels_default_pipeline ON vessels(default_pipeline_id);"
echo "  Index idx_fleets_default_pipeline: OK"
echo "  Index idx_vessels_default_pipeline: OK"

# ========================================
# Record schema migrations
# ========================================
echo ""
echo "--- Schema Version ---"

sqlite3 "$DB_PATH" <<'SQL'
CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (17, 'Add prompt_templates table', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (18, 'Add personas table', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (19, 'Add pipelines and pipeline_stages tables', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (20, 'Add captain persona columns (allowed_personas, preferred_persona)', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (21, 'Add mission persona and depends_on_mission_id columns', datetime('now'));
INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc)
VALUES (22, 'Add default_pipeline_id to fleets and vessels', datetime('now'));
SQL

echo "  Schema versions 17-22 recorded."

# ========================================
# Done
# ========================================
echo ""
echo "===================================="
echo "Migration v0.3.0 -> v0.4.0 complete!"
echo "===================================="
echo ""
echo "Summary of changes:"
echo "  Tables created:  prompt_templates, personas, pipelines, pipeline_stages"
echo "  Columns added:   captains.allowed_personas, captains.preferred_persona,"
echo "                   missions.persona, missions.depends_on_mission_id,"
echo "                   fleets.default_pipeline_id, vessels.default_pipeline_id"
echo "  Indexes created: 15 total (see above for details)"
echo ""
