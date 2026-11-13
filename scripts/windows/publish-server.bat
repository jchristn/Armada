@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Publish Armada.Server into %%USERPROFILE%%\.armada\bin and deploy the dashboard. Fails while an Armada server runs.
echo(Usage: scripts\windows\publish-server.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
echo(  ^<framework^>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
echo(  --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
exit /b 0
:armada_main

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
set "PUBLISH_DIR=%USERPROFILE%\.armada\bin"
set "SERVER_EXE=%PUBLISH_DIR%\Armada.Server.exe"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

echo.
echo [publish-server] Checking for a running Armada server...
call :armada_check_running
if defined ARMADA_RUNNING_PID goto :armada_running_error

echo.
echo [publish-server] Publishing Armada.Server for %ARMADA_TARGET_FRAMEWORK% to %PUBLISH_DIR%...
call dotnet publish "%REPO_ROOT%\src\Armada.Server" -c Release %ARMADA_DOTNET_FRAMEWORK_ARGS% -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b 1

rem A self-rebuild slot pointer would make start-armada-server.ps1 keep running the old slot instead of this publish.
if not exist "%PUBLISH_DIR%\current" goto :armada_slot_cleared
del /q "%PUBLISH_DIR%\current"
echo [publish-server] Cleared %PUBLISH_DIR%\current, so the start scripts run this publish rather than an older slot.
:armada_slot_cleared

echo.
echo [publish-server] Deploying dashboard assets...
call "%SCRIPT_DIR%\deploy-dashboard.bat"
if errorlevel 1 (
    if exist "%USERPROFILE%\.armada\dashboard\index.html" (
        echo [publish-server] WARNING: Dashboard deploy failed. Keeping the previously deployed React dashboard.
    ) else (
        echo ERROR: Dashboard deploy failed and no deployed React dashboard is available. 1>&2
        exit /b 1
    )
)

if not exist "%SERVER_EXE%" goto :armada_no_exe

echo.
echo [publish-server] Completed.
echo [publish-server] Server executable: %SERVER_EXE%
exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2

:armada_check_running
REM Sets ARMADA_RUNNING_PID to a running Admiral's PID (the published Armada.Server.exe or a dotnet host of
REM Armada.Server.dll), or clears it.
set "ARMADA_RUNNING_PID="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\stop-armada-server.ps1" -List`) do set "ARMADA_RUNNING_PID=%%P"
exit /b 0

:armada_running_error
echo ERROR: An Armada server is running (PID %ARMADA_RUNNING_PID%). Stop it with powershell -File scripts\windows\stop-armada-server.ps1 and try again. 1>&2
exit /b 1

:armada_no_exe
echo ERROR: Published server executable not found at %SERVER_EXE% 1>&2
exit /b 1
