@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Upgrade test: seed an older Armada, then verify the current build on its database (needs Git Bash).
echo(Usage: scripts\windows\run-upgrade-test.bat [--from-ref ^<git ref^>] [--providers sqlite,...^|all] [--framework net10.0] [--keep]
exit /b 0
:armada_main
REM Upgrade test (V1 readiness W3.1). Delegates to the common bash script
REM (requires Git Bash / WSL bash on PATH), which builds the baseline Armada
REM (default v0.9.0), seeds it, upgrades to this checkout, and verifies the
REM data on SQLite and, with Docker, PostgreSQL, MySQL, and SQL Server.
REM Example: scripts\windows\run-upgrade-test.bat --providers all
set "SCRIPT_DIR=%~dp0"
where bash >nul 2>nul
if errorlevel 1 (
  echo ERROR: 'bash' not found on PATH. Install Git for Windows ^(Git Bash^) and re-run. 1>&2
  echo The script needs git, dotnet, and ^(for server providers^) docker.
  exit /b 1
)
bash "%SCRIPT_DIR%..\common\run-upgrade-test.sh" %*
exit /b %errorlevel%
