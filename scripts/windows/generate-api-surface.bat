@echo off
setlocal
REM Regenerate the frozen public API surface (V1 readiness W2.1):
REM docs\api-surface-1.0.json and docs\API_SURFACE_1.0.md. Boots a throwaway
REM in-process Admiral through src\Test.Automated (never touches ~\.armada).
REM Usage: scripts\windows\generate-api-surface.bat [net10.0 | -f net10.0 | --framework net10.0] [--out <dir>]
REM Unknown arguments are rejected (exit 1), as in generate-api-surface.sh.
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\.."
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"
set "OUT_DIR=%REPO_ROOT%\docs"
:parse
if "%~1"=="" goto run
set "ARG=%~1"
if /I "%ARG%"=="--out" goto parse_out
if /I "%ARG%"=="-f" goto parse_framework
if /I "%ARG%"=="--framework" goto parse_framework
if /I "%ARG:~0,3%"=="net" (
    set "FRAMEWORK=%ARG%"
    shift
    goto parse
)
echo ERROR: unknown argument %ARG% 1>&2
goto usage
:parse_out
if "%~2"=="" (
    echo ERROR: --out needs a directory 1>&2
    goto usage
)
set "OUT_DIR=%~2"
shift
shift
goto parse
:parse_framework
if "%~2"=="" (
    echo ERROR: Missing framework value after %ARG%. 1>&2
    goto usage
)
set "FRAMEWORK=%~2"
shift
shift
goto parse
:usage
echo Usage: scripts\windows\generate-api-surface.bat [net10.0 ^| -f net10.0 ^| --framework net10.0] [--out ^<dir^>] 1>&2
exit /b 1
:run
dotnet run --project "%REPO_ROOT%\src\Test.Automated" --framework %FRAMEWORK% -- --generate-api-surface "%OUT_DIR%"
exit /b %errorlevel%
