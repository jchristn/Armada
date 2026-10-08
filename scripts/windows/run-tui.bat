@echo off
setlocal

REM Build and run the Armada terminal UI (armada tui, hosted by the CLI in src\Armada.Helm) from source.
REM
REM Usage:
REM   scripts\windows\run-tui.bat [-f <framework>|--framework <framework>|<framework>] [tui arguments...]
REM The framework (e.g. net8.0 or net10.0) is used to both build and run; default net10.0 (or ARMADA_TARGET_FRAMEWORK).
REM Any remaining arguments are passed to "armada tui", for example: scripts\windows\run-tui.bat net10.0 --profile work
REM The TUI talks to the Admiral in %USERPROFILE%\.armada\tui.json (profiles; see docs\TUI.md).

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"

REM Resolve the target framework (-f / --framework / bare net* / ARMADA_TARGET_FRAMEWORK / default net10.0).
call "%REPO_ROOT%\scripts\windows\resolve-framework.bat" %*
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

REM Drop the framework arguments the resolver consumed; forward the rest to armada tui.
set "TUI_ARGS="
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
set "TUI_ARGS=%TUI_ARGS% %1"
shift
goto :collect

:run
echo [run-tui] Building the Armada CLI (%FRAMEWORK%)...
dotnet build "%REPO_ROOT%\src\Armada.Helm\Armada.Helm.csproj" --framework %FRAMEWORK%
if errorlevel 1 (
    endlocal
    exit /b 1
)

echo [run-tui] Starting armada tui (%FRAMEWORK%)...
dotnet run --project "%REPO_ROOT%\src\Armada.Helm" --framework %FRAMEWORK% --no-build -- tui%TUI_ARGS%
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
