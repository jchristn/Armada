@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Pipelines Tables
echo =======================================
echo.
echo This script adds the pipelines and pipeline_stages tables, plus default_pipeline_id
echo columns to the fleets and vessels tables.
echo.

:: Check for sqlite3
where sqlite3 >nul 2>nul
if %errorlevel% neq 0 (
    echo ERROR: sqlite3 is required but not installed.
    echo Download from https://www.sqlite.org/download.html
    exit /b 1
)

:: Determine paths
if "%~1"=="" (
    set "SETTINGS_FILE=%USERPROFILE%\.armada\settings.json"
) else (
    set "SETTINGS_FILE=%~1"
)

echo Settings file: %SETTINGS_FILE%

if not exist "%SETTINGS_FILE%" (
    echo.
    echo ERROR: Settings file not found: %SETTINGS_FILE%
    echo.
    echo Usage: %~nx0 [path\to\settings.json]
    echo Default path: %%USERPROFILE%%\.armada\settings.json
    exit /b 1
)

:: Try to find database path from settings (simple parse)
set "DB_PATH=%USERPROFILE%\.armada\armada.db"

echo Database file: %DB_PATH%

if not exist "%DB_PATH%" (
    echo.
    echo ERROR: Database file not found: %DB_PATH%
    echo Make sure the Armada server has been run at least once to create the database.
    exit /b 1
)

:: Create backup
set "DB_BACKUP=%DB_PATH%.pre-pipelines.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Creating pipelines and pipeline_stages tables...

sqlite3.exe "%DB_PATH%" "CREATE TABLE pipelines (id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL, description TEXT, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id));"
if %errorlevel% neq 0 (
    echo WARNING: pipelines table may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE TABLE pipeline_stages (id TEXT PRIMARY KEY, pipeline_id TEXT NOT NULL, name TEXT NOT NULL, description TEXT, stage_order INTEGER NOT NULL, persona_name TEXT, auto_advance INTEGER NOT NULL DEFAULT 0, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (pipeline_id) REFERENCES pipelines(id));"
if %errorlevel% neq 0 (
    echo WARNING: pipeline_stages table may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE UNIQUE INDEX idx_pipelines_tenant_name ON pipelines(tenant_id, name);"
if %errorlevel% neq 0 (
    echo WARNING: idx_pipelines_tenant_name index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_pipelines_active ON pipelines(active);"
if %errorlevel% neq 0 (
    echo WARNING: idx_pipelines_active index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE UNIQUE INDEX idx_pipeline_stages_pipeline_order ON pipeline_stages(pipeline_id, stage_order);"
if %errorlevel% neq 0 (
    echo WARNING: idx_pipeline_stages_pipeline_order index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_pipeline_stages_pipeline_id ON pipeline_stages(pipeline_id);"
if %errorlevel% neq 0 (
    echo WARNING: idx_pipeline_stages_pipeline_id index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_pipeline_stages_persona ON pipeline_stages(persona_name);"
if %errorlevel% neq 0 (
    echo WARNING: idx_pipeline_stages_persona index may already exist.
)

sqlite3.exe "%DB_PATH%" "ALTER TABLE fleets ADD COLUMN default_pipeline_id TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: fleets.default_pipeline_id column may already exist.
)

sqlite3.exe "%DB_PATH%" "ALTER TABLE vessels ADD COLUMN default_pipeline_id TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: vessels.default_pipeline_id column may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_fleets_default_pipeline ON fleets(default_pipeline_id);"
if %errorlevel% neq 0 (
    echo WARNING: idx_fleets_default_pipeline index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_vessels_default_pipeline ON vessels(default_pipeline_id);"
if %errorlevel% neq 0 (
    echo WARNING: idx_vessels_default_pipeline index may already exist.
)

echo.
echo Migration complete.
echo.
echo New tables created: pipelines, pipeline_stages
echo New columns added: fleets.default_pipeline_id, vessels.default_pipeline_id
echo   Indexes: idx_pipelines_tenant_name (unique), idx_pipelines_active,
echo            idx_pipeline_stages_pipeline_order (unique), idx_pipeline_stages_pipeline_id,
echo            idx_pipeline_stages_persona, idx_fleets_default_pipeline,
echo            idx_vessels_default_pipeline
echo.

endlocal
