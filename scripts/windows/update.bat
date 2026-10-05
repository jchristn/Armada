@echo off
setlocal

rem =====================================================================
rem update.bat -- updates a GLOBAL-TOOL / MANUAL install (see install.bat).
rem Stops the server via the Armada.Helm CLI, reinstalls the global
rem Armada.Helm dotnet tool + redeploys the dashboard, then restarts.
rem This is NOT the same as update-windows-task.bat, which updates the
rem registered Windows auto-start deployment (HKCU Run key "ArmadaAdmiral",
rem self-contained server published to %USERPROFILE%\.armada\bin). Use the
rem update script that matches how you installed; they are not interchangeable.
rem =====================================================================

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
set "HELM_DLL=%REPO_ROOT%\src\Armada.Helm\bin\Debug\%ARMADA_TARGET_FRAMEWORK%\Armada.Helm.dll"

echo.
echo [update] Stopping repo-backed Armada MCP stdio hosts if they are running...
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\stop-repo-mcp-hosts.ps1" -RepoRoot "%REPO_ROOT%"
if errorlevel 1 exit /b 1

echo.
echo [update] Stopping Armada server if it is running...
call :run_helm server stop

echo.
echo [update] Stopping every remaining Armada.Server process...
rem PowerShell helper stops managed and dotnet-hosted instances and waits for exit.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\stop-armada-server.ps1"
rem Batch-level guarantee: force-kill any Armada.Server.exe by image name regardless of path,
rem so a server launched straight out of the repo bin cannot survive and hold the port/DLL locks.
taskkill /F /IM Armada.Server.exe >nul 2>nul

echo.
echo [update] Reinstalling Armada tool and redeploying dashboard...
call "%SCRIPT_DIR%\reinstall.bat" %ARMADA_FORWARD_FRAMEWORK_ARGS%
if errorlevel 1 exit /b 1

echo.
echo [update] Starting Armada server...
call :run_helm server start
exit /b %ERRORLEVEL%

:run_helm
if exist "%HELM_DLL%" (
  call dotnet "%HELM_DLL%" %*
  exit /b %ERRORLEVEL%
)

call dotnet run --project "%REPO_ROOT%\src\Armada.Helm" %ARMADA_DOTNET_FRAMEWORK_ARGS% -- %*
if not errorlevel 1 exit /b 0

where armada >nul 2>nul
if not errorlevel 1 (
  armada %*
  exit /b %ERRORLEVEL%
)

exit /b 1
