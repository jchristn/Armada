@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Captain Persona Fields
echo =============================================
echo.
echo This script adds allowed_personas and preferred_persona columns to the captains table.
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
set "DB_BACKUP=%DB_PATH%.pre-captain-personas.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Adding allowed_personas and preferred_persona columns to captains table...

sqlite3.exe "%DB_PATH%" "ALTER TABLE captains ADD COLUMN allowed_personas TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: allowed_personas column may already exist.
)

sqlite3.exe "%DB_PATH%" "ALTER TABLE captains ADD COLUMN preferred_persona TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: preferred_persona column may already exist.
)

sqlite3.exe "%DB_PATH%" "CREATE INDEX idx_captains_preferred_persona ON captains(preferred_persona);"
if %errorlevel% neq 0 (
    echo WARNING: idx_captains_preferred_persona index may already exist.
)

echo.
echo Migration complete.
echo.
echo New columns added to captains table:
echo   - allowed_personas (TEXT, nullable, JSON array of persona names)
echo   - preferred_persona (TEXT, nullable)
echo   Index: idx_captains_preferred_persona
echo.

endlocal
