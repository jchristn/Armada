@echo off
setlocal

REM Build and run Armada locally for development: builds the server and Harbor, launches the server in its
REM own window, waits for it to become healthy, then launches Harbor in its own window.
REM
REM Usage:
REM   run-local.bat [-f <framework>|--framework <framework>|<framework>]
REM The framework (e.g. net8.0 or net10.0) is used to BOTH build and run; default net10.0.

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"

REM Resolve the target framework (-f / --framework / bare net* / ARMADA_TARGET_FRAMEWORK / default net10.0).
call "%SCRIPT_DIR%\resolve-framework.bat" %*
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

set "BASE_URL=%ARMADA_BASE_URL%"
if "%BASE_URL%"=="" set "BASE_URL=http://localhost:7890"

echo [run-local] Building server + Harbor (framework %FRAMEWORK%)...
dotnet build "%REPO_ROOT%\src\Armada.Server\Armada.Server.csproj" --framework %FRAMEWORK%
if errorlevel 1 exit /b 1
dotnet build "%REPO_ROOT%\src\Armada.Harbor\Armada.Harbor.csproj" --framework %FRAMEWORK%
if errorlevel 1 exit /b 1

echo [run-local] Starting Armada Server (%FRAMEWORK%)...
start "Armada Server" cmd /k dotnet run --project "%REPO_ROOT%\src\Armada.Server" --framework %FRAMEWORK% --no-build

echo [run-local] Waiting for server health at %BASE_URL%/api/v1/status/health ...
call "%SCRIPT_DIR%\healthcheck-server.bat" %FRAMEWORK% "%BASE_URL%"

echo [run-local] Starting Armada Harbor (%FRAMEWORK%)...
start "Armada Harbor" cmd /k dotnet run --project "%REPO_ROOT%\src\Armada.Harbor" --framework %FRAMEWORK% --no-build

echo [run-local] Server and Harbor launched in separate windows. Close those windows to stop them.
endlocal
