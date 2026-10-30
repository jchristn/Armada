@echo off
setlocal
REM Regenerate docs\blackbox-record.schema.json, the JSON Schema of the black box
REM record.json and its published shapes, from the C# models (BlackBoxSchemaGenerator).
REM The Models.BlackBoxSchema test fails when the committed file drifts from the models.
REM Usage: scripts\windows\generate-blackbox-schema.bat [net10.0 | -f net10.0 | --framework net10.0] [--out <dir>]
REM Unknown arguments are rejected (exit 1), as in generate-blackbox-schema.sh.
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
echo Usage: scripts\windows\generate-blackbox-schema.bat [net10.0 ^| -f net10.0 ^| --framework net10.0] [--out ^<dir^>] 1>&2
exit /b 1
:run
dotnet run --project "%REPO_ROOT%\src\Test.Automated" --framework %FRAMEWORK% -p:TargetFramework=%FRAMEWORK% -p:TargetFrameworks=%FRAMEWORK% -- --generate-blackbox-schema "%OUT_DIR%"
exit /b %errorlevel%
