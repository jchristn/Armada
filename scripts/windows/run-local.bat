@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Build the server and Harbor, deploy the dashboard, start the server, wait for its health, then start Harbor.
echo(Set ARMADA_SKIP_DASHBOARD=1 to keep the deployed dashboard.
echo(Usage: scripts\windows\run-local.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
exit /b 0
:armada_main

REM Build and run Armada locally for development: builds the server and Harbor, builds and deploys the
REM dashboard (deploy-dashboard.bat, into %USERPROFILE%\.armada\dashboard where the server serves it from),
REM launches the server in its own window, waits for it to become healthy, then launches Harbor in its own window.
REM
REM Usage:
REM   run-local.bat [-f <framework>|--framework <framework>|<framework>]
REM The framework (e.g. net8.0 or net10.0) is used to BOTH build and run; default net10.0.
REM Set ARMADA_SKIP_DASHBOARD=1 to skip the dashboard build and keep the currently deployed dashboard.

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

REM Resolve the target framework (-f / --framework / bare net* / ARMADA_TARGET_FRAMEWORK / default net10.0).
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

set "BASE_URL=%ARMADA_BASE_URL%"
if "%BASE_URL%"=="" set "BASE_URL=http://localhost:7890"

echo [run-local] Building server + Harbor (framework %FRAMEWORK%)...
dotnet build "%REPO_ROOT%\src\Armada.Server\Armada.Server.csproj" %ARMADA_DOTNET_FRAMEWORK_ARGS%
if errorlevel 1 exit /b 1
dotnet build "%REPO_ROOT%\src\Armada.Harbor\Armada.Harbor.csproj" %ARMADA_DOTNET_FRAMEWORK_ARGS%
if errorlevel 1 exit /b 1

if "%ARMADA_SKIP_DASHBOARD%"=="1" (
    echo [run-local] Skipping dashboard build ^(ARMADA_SKIP_DASHBOARD=1^).
) else (
    call "%SCRIPT_DIR%\deploy-dashboard.bat" %*
    if errorlevel 1 exit /b 1
)

echo [run-local] Starting Armada Server (%FRAMEWORK%)...
start "Armada Server" cmd /k dotnet run --project "%REPO_ROOT%\src\Armada.Server" --framework %FRAMEWORK% --no-build

echo [run-local] Waiting for server health at %BASE_URL%/api/v1/status/health ...
call "%SCRIPT_DIR%\healthcheck-server.bat" %FRAMEWORK% "%BASE_URL%"

echo [run-local] Starting Armada Harbor (%FRAMEWORK%)...
start "Armada Harbor" cmd /k dotnet run --project "%REPO_ROOT%\src\Armada.Harbor" --framework %FRAMEWORK% --no-build

echo [run-local] Server and Harbor launched in separate windows. Close those windows to stop them.
endlocal & exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2
