@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Run the Database suite against SQLite, PostgreSQL, MySQL and SQL Server (needs Git Bash and Docker).
echo(Usage: scripts\windows\run-db-parity-tests.bat [--providers sqlite,postgresql,mysql,sqlserver] [--framework net10.0] [--no-build] [--suites Database]
exit /b 0
:armada_main
REM Delegates to the common bash script (requires Git Bash / WSL bash on PATH),
REM which orchestrates the Docker containers and runs the full Database suite
REM against every provider.
set "SCRIPT_DIR=%~dp0"
where bash >nul 2>nul
if errorlevel 1 (
  echo ERROR: 'bash' not found on PATH. Install Git for Windows ^(Git Bash^) or run 1>&2
  echo the suite manually, e.g.:
  echo   dotnet run --project src\Test.Automated --framework net10.0 -- --db-type postgresql --db-host 127.0.0.1 --db-port 5432 --db-user postgres --db-pass ^<pw^> --db-name armada_test --suites Database
  exit /b 1
)
bash "%SCRIPT_DIR%..\common\run-db-parity-tests.sh" %*
exit /b %errorlevel%
