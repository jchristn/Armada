@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: Add Mission Agent Output
echo ============================================
echo.
echo This script adds agent_output column to the missions table.
echo This field stores accumulated agent stdout captured during mission
echo execution, used by architect missions for marker parsing and by
echo pipeline handoff to pass context to the next stage.
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
set "DB_BACKUP=%DB_PATH%.pre-agent-output.bak"
copy "%DB_PATH%" "%DB_BACKUP%" >nul
echo Database backup created: %DB_BACKUP%

echo.
echo Adding agent_output column to missions table...

sqlite3 "%DB_PATH%" "ALTER TABLE missions ADD COLUMN agent_output TEXT;"
if %errorlevel% neq 0 (
    echo WARNING: agent_output column may already exist.
)

echo.
echo Migration complete.
echo.
echo New column added to missions table:
echo   - agent_output (TEXT, nullable)
echo.
echo This column is populated automatically when agents complete missions.

endlocal
