@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Migration: v0.4.0 to v0.5.0
echo ====================================
echo.
echo This script prepares the schema changes for v0.5.0:
echo   1. Adds captains.model
echo   2. Adds missions.total_runtime_ms
echo.
echo SQLite is applied automatically from the local Armada settings file.
echo SQL for PostgreSQL, MySQL, and SQL Server is included below for manual execution.
echo.

where sqlite3 >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: sqlite3 is required but not found on PATH.
    echo Download from https://www.sqlite.org/download.html
    exit /b 1
)

where powershell >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: PowerShell is required but not found on PATH.
    exit /b 1
)

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
    echo Default path: %USERPROFILE%\.armada\settings.json
    exit /b 1
)

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

set "DB_BACKUP=!DB_PATH!.pre-v0.5.0.bak"
copy /y "!DB_PATH!" "!DB_BACKUP!" >nul
echo Database backup created: !DB_BACKUP!

echo.
echo --- SQLite ---

sqlite3 "!DB_PATH!" "ALTER TABLE captains ADD COLUMN model TEXT;" 2>nul
sqlite3 "!DB_PATH!" "ALTER TABLE missions ADD COLUMN total_runtime_ms INTEGER;" 2>nul
sqlite3 "!DB_PATH!" "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_utc TEXT NOT NULL);"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (26, 'Add model to captains', datetime('now'));"
sqlite3 "!DB_PATH!" "INSERT OR IGNORE INTO schema_migrations (version, description, applied_utc) VALUES (27, 'Add total_runtime_ms to missions', datetime('now'));"

echo   captains.model: OK
echo   missions.total_runtime_ms: OK
echo   Schema versions 26-27 recorded.

echo.
echo --- PostgreSQL SQL ---
echo ALTER TABLE captains ADD COLUMN model TEXT;
echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT;
echo INSERT INTO schema_migrations (version, description, applied_utc^) VALUES (26, 'Add model to captains', NOW()^) ON CONFLICT (version^) DO NOTHING;
echo INSERT INTO schema_migrations (version, description, applied_utc^) VALUES (27, 'Add total_runtime_ms to missions', NOW()^) ON CONFLICT (version^) DO NOTHING;

echo.
echo --- MySQL SQL ---
echo ALTER TABLE captains ADD COLUMN model TEXT;
echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT;
echo CREATE TABLE IF NOT EXISTS schema_migrations (version INT NOT NULL PRIMARY KEY, description LONGTEXT NOT NULL, applied_utc DATETIME(6^) NOT NULL^);
echo INSERT IGNORE INTO schema_migrations (version, description, applied_utc^) VALUES (26, 'Add model to captains', UTC_TIMESTAMP(6^)^);
echo INSERT IGNORE INTO schema_migrations (version, description, applied_utc^) VALUES (27, 'Add total_runtime_ms to missions', UTC_TIMESTAMP(6^)^);

echo.
echo --- SQL Server SQL ---
echo ALTER TABLE captains ADD model NVARCHAR(MAX^) NULL;
echo ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;
echo IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[schema_migrations]'^) AND type = N'U'^)
echo BEGIN
echo     CREATE TABLE schema_migrations (version INT NOT NULL PRIMARY KEY, description NVARCHAR(MAX^) NOT NULL, applied_utc DATETIME2 NOT NULL^);
echo END;
echo IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 26^)
echo BEGIN
echo     INSERT INTO schema_migrations (version, description, applied_utc^) VALUES (26, 'Add model to captains', SYSUTCDATETIME()^);
echo END;
echo IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = 27^)
echo BEGIN
echo     INSERT INTO schema_migrations (version, description, applied_utc^) VALUES (27, 'Add total_runtime_ms to missions', SYSUTCDATETIME()^);
echo END;

echo.
echo ====================================
echo Migration v0.4.0 -^> v0.5.0 complete!
echo ====================================
echo.
echo Summary of changes:
echo   SQLite: applied captains.model and missions.total_runtime_ms
echo   PostgreSQL: SQL block emitted above
echo   MySQL: SQL block emitted above
echo   SQL Server: SQL block emitted above

endlocal
