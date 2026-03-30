@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: v0.3.0 to v0.4.0
echo ====================================
echo.
echo This script performs the following database migrations:
echo   1. Creates prompt_templates table + indexes
echo   2. Creates personas table + indexes
echo   3. Creates pipelines and pipeline_stages tables + indexes
echo   4. Adds allowed_personas and preferred_persona columns to captains
echo   5. Adds persona and depends_on_mission_id columns to missions
echo   6. Adds default_pipeline_id column to fleets and vessels
echo.
echo All operations are idempotent and safe to run multiple times.
echo.

REM Check for sqlite3
where sqlite3 >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: sqlite3 is required but not found on PATH.
    echo Download from https://www.sqlite.org/download.html
    exit /b 1
)

REM Check for PowerShell
where powershell >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: PowerShell is required but not found on PATH.
    exit /b 1
)

REM Determine settings file path
if "%~1"=="" (
    set "SETTINGS_FILE=%USERPROFILE%\.armada\settings.json"
) else (
    set "SETTINGS_FILE=%~1"
)

echo Settings file: %SETTINGS_FILE%

REM Check if the file exists
if not exist "%SETTINGS_FILE%" (
    echo.
    echo ERROR: Settings file not found: %SETTINGS_FILE%
    echo.
    echo Usage: %~nx0 [path\to\settings.json]
    echo Default path: %USERPROFILE%\.armada\settings.json
    exit /b 1
)

REM Determine database path from settings
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "$json = Get-Content '%SETTINGS_FILE%' -Raw | ConvertFrom-Json; if ($json.database -and $json.database.filename) { $json.database.filename } elseif ($json.databasePath) { $json.databasePath }"`) do set "DB_PATH=%%i"

if "!DB_PATH!"=="" (
    set "DB_PATH=%USERPROFILE%\.armada\armada.db"
)

echo Database file: !DB_PATH!

if not exist "!DB_PATH!" (
    echo.
    echo ERROR: Database file not found: !DB_PATH!
    echo Make sure the Armada server has been run at least once to create the database.
    exit /b 1
)

REM Create backup
set "DB_BACKUP=!DB_PATH!.pre-v0.4.0.bak"
copy /y "!DB_PATH!" "!DB_BACKUP!" >nul
echo Database backup created: !DB_BACKUP!

REM ========================================
REM 1. prompt_templates table
REM ========================================
echo.
echo --- 1. Prompt Templates ---

sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS prompt_templates (id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL, description TEXT, category TEXT NOT NULL DEFAULT 'mission', content TEXT NOT NULL, is_built_in INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id));"
sqlite3 "!DB_PATH!" "CREATE UNIQUE INDEX IF NOT EXISTS idx_prompt_templates_tenant_name ON prompt_templates(tenant_id, name);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_prompt_templates_category ON prompt_templates(category);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_prompt_templates_active ON prompt_templates(active);"

echo   prompt_templates table: OK

REM ========================================
REM 2. personas table
REM ========================================
echo.
echo --- 2. Personas ---

sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS personas (id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL, description TEXT, prompt_template_name TEXT NOT NULL, is_built_in INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id));"
sqlite3 "!DB_PATH!" "CREATE UNIQUE INDEX IF NOT EXISTS idx_personas_tenant_name ON personas(tenant_id, name);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_personas_active ON personas(active);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_personas_prompt_template ON personas(prompt_template_name);"

echo   personas table: OK

REM ========================================
REM 3. pipelines and pipeline_stages tables
REM ========================================
echo.
echo --- 3. Pipelines ---

sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS pipelines (id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL, description TEXT, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id));"
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS pipeline_stages (id TEXT PRIMARY KEY, pipeline_id TEXT NOT NULL, name TEXT NOT NULL, description TEXT, stage_order INTEGER NOT NULL, persona_name TEXT, auto_advance INTEGER NOT NULL DEFAULT 0, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (pipeline_id) REFERENCES pipelines(id));"
sqlite3 "!DB_PATH!" "CREATE UNIQUE INDEX IF NOT EXISTS idx_pipelines_tenant_name ON pipelines(tenant_id, name);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_pipelines_active ON pipelines(active);"
sqlite3 "!DB_PATH!" "CREATE UNIQUE INDEX IF NOT EXISTS idx_pipeline_stages_pipeline_order ON pipeline_stages(pipeline_id, stage_order);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_pipeline_stages_pipeline_id ON pipeline_stages(pipeline_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_pipeline_stages_persona ON pipeline_stages(persona_name);"

echo   pipelines table: OK
echo   pipeline_stages table: OK

REM ========================================
REM 4. captains: add persona columns
REM ========================================
echo.
echo --- 4. Captain Persona Columns ---

sqlite3 "!DB_PATH!" "ALTER TABLE captains ADD COLUMN allowed_personas TEXT;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE captains ADD COLUMN preferred_persona TEXT;" 2>nul
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_captains_preferred_persona ON captains(preferred_persona);"

echo   captains.allowed_personas: OK
echo   captains.preferred_persona: OK
echo   Index idx_captains_preferred_persona: OK

REM ========================================
REM 5. missions: add persona and dependency columns
REM ========================================
echo.
echo --- 5. Mission Persona and Dependency Columns ---

sqlite3 "!DB_PATH!" "ALTER TABLE missions ADD COLUMN persona TEXT;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE missions ADD COLUMN depends_on_mission_id TEXT;" 2>nul
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_persona ON missions(persona);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_missions_depends_on ON missions(depends_on_mission_id);"

echo   missions.persona: OK
echo   missions.depends_on_mission_id: OK
echo   Index idx_missions_persona: OK
echo   Index idx_missions_depends_on: OK

REM ========================================
REM 6. fleets and vessels: add default_pipeline_id
REM ========================================
echo.
echo --- 6. Default Pipeline Columns ---

sqlite3 "!DB_PATH!" "ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;" 2>nul
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_fleets_default_pipeline ON fleets(default_pipeline_id);"
sqlite3 "!DB_PATH!" "CREATE INDEX IF NOT EXISTS idx_vessels_default_pipeline ON vessels(default_pipeline_id);"

echo   fleets.default_pipeline_id: OK
echo   vessels.default_pipeline_id: OK
echo   Index idx_fleets_default_pipeline: OK
echo   Index idx_vessels_default_pipeline: OK

REM ========================================
REM Record schema migrations
REM ========================================
echo.
echo --- Schema Version ---

sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_utc TEXT NOT NULL);"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (17, 'Add prompt_templates table', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (18, 'Add personas table', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (19, 'Add pipelines and pipeline_stages tables', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (20, 'Add captain persona columns (allowed_personas, preferred_persona)', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (21, 'Add mission persona and depends_on_mission_id columns', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (22, 'Add default_pipeline_id to fleets and vessels', datetime('now'));"

echo   Schema versions 17-22 recorded.

REM ========================================
REM Done
REM ========================================
echo.
echo ====================================
echo Migration v0.3.0 -^> v0.4.0 complete!
echo ====================================
echo.
echo Summary of changes:
echo   Tables created:  prompt_templates, personas, pipelines, pipeline_stages
echo   Columns added:   captains.allowed_personas, captains.preferred_persona,
echo                    missions.persona, missions.depends_on_mission_id,
echo                    fleets.default_pipeline_id, vessels.default_pipeline_id
echo   Indexes created: 15 total (see above for details)
echo.

exit /b 0
