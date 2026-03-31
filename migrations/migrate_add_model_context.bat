@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Model Context Fields
echo ============================================
echo.
echo This script adds EnableModelContext and ModelContext columns to the vessels table.
echo These fields enable AI agents to accumulate knowledge about repositories across missions.
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
set "DB_BACKUP=%DB_PATH%.pre-model-context.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Adding enable_model_context and model_context columns to vessels table...

sqlite3 "%DB_PATH%" "ALTER TABLE vessels ADD COLUMN enable_model_context INTEGER NOT NULL DEFAULT 1;"
if %errorlevel% neq 0 (
    echo WARNING: enable_model_context column may already exist.
)

sqlite3 "%DB_PATH%" "ALTER TABLE vessels ADD COLUMN model_context TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: model_context column may already exist.
)

echo.
echo Migration complete.
echo.
echo New columns added to vessels table:
echo   - enable_model_context (INTEGER, default 0)
echo   - model_context (TEXT, nullable)
echo.
echo To enable model context for a vessel, set enable_model_context to 1
echo via the dashboard, REST API, or MCP tools.

endlocal
