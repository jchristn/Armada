@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Mission Persona and Dependency Fields
echo ===========================================================
echo.
echo This script adds persona and depends_on_mission_id columns to the missions table.
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
set "DB_BACKUP=%DB_PATH%.pre-mission-persona.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Adding persona and depends_on_mission_id columns to missions table...

sqlite3.exe "%DB_PATH%" "ALTER TABLE missions ADD COLUMN persona TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: persona column may already exist.
)

sqlite3.exe "%DB_PATH%" "ALTER TABLE missions ADD COLUMN depends_on_mission_id TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: depends_on_mission_id column may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_missions_persona ON missions(persona);"
if %errorlevel% neq 0 (
    echo WARNING: idx_missions_persona index may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_missions_depends_on ON missions(depends_on_mission_id);"
if %errorlevel% neq 0 (
    echo WARNING: idx_missions_depends_on index may already exist.
)

echo.
echo Migration complete.
echo.
echo New columns added to missions table:
echo   - persona (TEXT, nullable, persona name for this mission)
echo   - depends_on_mission_id (TEXT, nullable, mission ID this depends on)
echo   Indexes: idx_missions_persona, idx_missions_depends_on
echo.

endlocal
