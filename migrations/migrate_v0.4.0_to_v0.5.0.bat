@echo off
setlocal

set "BACKEND=%~1"
if "%BACKEND%"=="" set "BACKEND=all"

if /I "%BACKEND%"=="sqlite" goto sqlite
if /I "%BACKEND%"=="postgresql" goto postgresql
if /I "%BACKEND%"=="postgres" goto postgresql
if /I "%BACKEND%"=="pgsql" goto postgresql
if /I "%BACKEND%"=="sqlserver" goto sqlserver
if /I "%BACKEND%"=="mssql" goto sqlserver
if /I "%BACKEND%"=="mysql" goto mysql
if /I "%BACKEND%"=="all" goto all

echo Usage: %~nx0 [sqlite^|postgresql^|sqlserver^|mysql^|all]
exit /b 1

:all
call :sqlite
echo.
call :postgresql
echo.
call :sqlserver
echo.
call :mysql
exit /b 0

:sqlite
echo -- SQLite
echo ALTER TABLE captains ADD COLUMN model TEXT NULL;
echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
echo INSERT OR IGNORE INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(26, 'Add model to captains', datetime^('now'^)^);
echo INSERT OR IGNORE INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(27, 'Add total_runtime_ms to missions', datetime^('now'^)^);
exit /b 0

:postgresql
echo -- PostgreSQL
echo ALTER TABLE captains ADD COLUMN model TEXT NULL;
echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
echo INSERT INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(26, 'Add model to captains', NOW^(^)^) ON CONFLICT ^(version^) DO NOTHING;
echo INSERT INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(27, 'Add total_runtime_ms to missions', NOW^(^)^) ON CONFLICT ^(version^) DO NOTHING;
exit /b 0

:sqlserver
echo -- SQL Server
echo ALTER TABLE captains ADD model NVARCHAR^(MAX^) NULL;
echo ALTER TABLE missions ADD total_runtime_ms BIGINT NULL;
echo IF NOT EXISTS ^(SELECT 1 FROM schema_migrations WHERE version = 26^)
echo     INSERT INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(26, 'Add model to captains', SYSUTCDATETIME^(^)^);
echo IF NOT EXISTS ^(SELECT 1 FROM schema_migrations WHERE version = 27^)
echo     INSERT INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(27, 'Add total_runtime_ms to missions', SYSUTCDATETIME^(^)^);
exit /b 0

:mysql
echo -- MySQL
echo ALTER TABLE captains ADD COLUMN model TEXT NULL;
echo ALTER TABLE missions ADD COLUMN total_runtime_ms BIGINT NULL;
echo INSERT IGNORE INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(26, 'Add model to captains', UTC_TIMESTAMP^(6^)^);
echo INSERT IGNORE INTO schema_migrations ^(version, description, applied_utc^) VALUES ^(27, 'Add total_runtime_ms to missions', UTC_TIMESTAMP^(6^)^);
exit /b 0
