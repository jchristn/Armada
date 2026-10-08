@echo off
setlocal

REM Build and run Armada Harbor (the host-side runner, src\Armada.Harbor) from source.
REM
REM Usage:
REM   scripts\windows\run-harbor.bat [-f <framework>|--framework <framework>|<framework>] [harbor arguments...]
REM The framework (e.g. net8.0 or net10.0) is used to both build and run; default net10.0 (or ARMADA_TARGET_FRAMEWORK).
REM Any remaining arguments are passed to Harbor, for example: scripts\windows\run-harbor.bat net10.0 --install-startup
REM Harbor's server link URL, access key, and capabilities are set in its Settings window (see docs\HARBOR.md).

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"

REM Resolve the target framework (-f / --framework / bare net* / ARMADA_TARGET_FRAMEWORK / default net10.0).
call "%REPO_ROOT%\scripts\windows\resolve-framework.bat" %*
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

REM Drop the framework arguments the resolver consumed; forward the rest to Harbor.
set "HARBOR_ARGS="
set "SKIP=0"
if /i "%~1"=="-f" set "SKIP=2"
if /i "%~1"=="--framework" set "SKIP=2"
set "FIRST=%~1"
if "%SKIP%"=="0" if /i "%FIRST:~0,3%"=="net" set "SKIP=1"
:skip_loop
if "%SKIP%"=="0" goto :collect
shift
set /a SKIP-=1
goto :skip_loop
:collect
if "%~1"=="" goto :run
set "HARBOR_ARGS=%HARBOR_ARGS% %1"
shift
goto :collect

:run
echo [run-harbor] Building Armada Harbor (%FRAMEWORK%)...
dotnet build "%REPO_ROOT%\src\Armada.Harbor\Armada.Harbor.csproj" --framework %FRAMEWORK%
if errorlevel 1 (
    endlocal
    exit /b 1
)

echo [run-harbor] Starting Armada Harbor (%FRAMEWORK%)...
dotnet run --project "%REPO_ROOT%\src\Armada.Harbor" --framework %FRAMEWORK% --no-build -- %HARBOR_ARGS%
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
