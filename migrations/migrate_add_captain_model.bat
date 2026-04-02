@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Captain Model
echo ====================================
echo.
echo This script adds model column to the captains table.
echo This field stores an optional per-captain model override.
echo When null, the runtime selects its default model.
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

set "DB_PATH=%USERPROFILE%\.armada\armada.db"

echo Database file: %DB_PATH%

if not exist "%DB_PATH%" (
    echo.
    echo ERROR: Database file not found: %DB_PATH%
    echo Make sure the Armada server has been run at least once to create the database.
    exit /b 1
)

:: Create backup
set "DB_BACKUP=%DB_PATH%.pre-captain-model.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Adding model column to captains table...

sqlite3 "%DB_PATH%" "ALTER TABLE captains ADD COLUMN model TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: model column may already exist.
)

echo.
echo Migration complete.
echo.
echo New column added to captains table:
echo   - model ^(TEXT, nullable^)
echo.
echo Set this field to override the model used when the captain launches.

endlocal
