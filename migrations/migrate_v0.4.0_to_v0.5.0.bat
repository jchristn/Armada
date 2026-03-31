@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: v0.4.0 to v0.5.0
echo ====================================
echo.
echo This script performs the following database migrations:
echo   1. Adds model column to captains (AI model selection)
echo   2. Adds total_runtime_seconds column to missions
echo.
echo All operations are idempotent and safe to run multiple times.
echo.

:: Check for sqlite3
where sqlite3 >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: sqlite3 is required but not installed.
    echo.
    echo Download sqlite3 from https://www.sqlite.org/download.html
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

:: Default database path
set "DB_PATH=%USERPROFILE%\.armada\armada.db"

echo Database file: %DB_PATH%

if not exist "%DB_PATH%" (
    echo.
    echo ERROR: Database file not found: %DB_PATH%
    echo Make sure the Armada server has been run at least once to create the database.
    exit /b 1
)

:: Create backup
set "DB_BACKUP=%DB_PATH%.pre-v0.5.0.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

:: 1. Add model column to captains
echo.
echo --- 1. Captain Model ---
sqlite3 "%DB_PATH%" "ALTER TABLE captains ADD COLUMN model TEXT;" 2>nul
echo   model column: OK

:: 2. Add total_runtime_seconds to missions
echo.
echo --- 2. Mission Total Runtime ---
sqlite3 "%DB_PATH%" "ALTER TABLE missions ADD COLUMN total_runtime_seconds REAL;" 2>nul
echo   total_runtime_seconds column: OK

:: Record schema versions
echo.
echo --- Recording schema versions ---
sqlite3 "%DB_PATH%" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains for specifying AI model', datetime('now'));"
sqlite3 "%DB_PATH%" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_seconds to missions', datetime('now'));"
echo   Schema versions 26-27 recorded: OK

echo.
echo ====================================
echo Migration complete! v0.4.0 -^> v0.5.0
echo ====================================
echo.
echo Backup file: %DB_BACKUP%
echo You can remove the backup once you verify everything works.
