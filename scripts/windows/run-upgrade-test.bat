@echo off
setlocal
REM Upgrade test (V1 readiness W3.1). Delegates to the common bash script
REM (requires Git Bash / WSL bash on PATH), which builds the baseline Armada
REM (default v0.9.0), seeds it, upgrades to this checkout, and verifies the
REM data on SQLite and, with Docker, PostgreSQL, MySQL, and SQL Server.
REM Example: scripts\windows\run-upgrade-test.bat --providers all
set "SCRIPT_DIR=%~dp0"
where bash >nul 2>nul
if errorlevel 1 (
  echo ERROR: 'bash' not found on PATH. Install Git for Windows ^(Git Bash^) and re-run.
  echo The script needs git, dotnet, and ^(for server providers^) docker.
  exit /b 1
)
bash "%SCRIPT_DIR%..\common\run-upgrade-test.sh" %*
exit /b %errorlevel%
