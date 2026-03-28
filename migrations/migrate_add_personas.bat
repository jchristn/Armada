@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Personas Table
echo =====================================
echo.
echo This script adds the personas table for named agent roles.
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
set "DB_BACKUP=%DB_PATH%.pre-personas.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Creating personas table...

sqlite3.exe "%DB_PATH%" "CREATE TABLE personas (id TEXT PRIMARY KEY, tenant_id TEXT, name TEXT NOT NULL, description TEXT, prompt_template_name TEXT NOT NULL, is_built_in INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, last_update_utc TEXT NOT NULL, FOREIGN KEY (tenant_id) REFERENCES tenants(id));"
if %errorlevel% neq 0 (
    echo WARNING: personas table may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE UNIQUE INDEX idx_personas_tenant_name ON personas(tenant_id, name);"
if %errorlevel% neq 0 (
    echo WARNING: idx_personas_tenant_name index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_personas_active ON personas(active);"
if %errorlevel% neq 0 (
    echo WARNING: idx_personas_active index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_personas_prompt_template ON personas(prompt_template_name);"
if %errorlevel% neq 0 (
    echo WARNING: idx_personas_prompt_template index may already exist.
)

echo.
echo Migration complete.
echo.
echo New table created: personas
echo   Indexes: idx_personas_tenant_name (unique), idx_personas_active, idx_personas_prompt_template
echo.

endlocal
