@echo off
setlocal
REM Regenerate the frozen public API surface (V1 readiness W2.1):
REM docs\api-surface-1.0.json and docs\API_SURFACE_1.0.md. Boots a throwaway
REM in-process Admiral through src\Test.Automated (never touches ~\.armada).
REM Usage: scripts\windows\generate-api-surface.bat [net10.0] [--out <dir>]
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\.."
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"
set "OUT_DIR=%REPO_ROOT%\docs"
:parse
if "%~1"=="" goto run
if /I "%~1"=="--out" (set "OUT_DIR=%~2" & shift & shift & goto parse)
if /I "%~1"=="-f" (set "FRAMEWORK=%~2" & shift & shift & goto parse)
if /I "%~1"=="--framework" (set "FRAMEWORK=%~2" & shift & shift & goto parse)
set "FRAMEWORK=%~1"
shift
goto parse
:run
dotnet run --project "%REPO_ROOT%\src\Test.Automated" --framework %FRAMEWORK% -- --generate-api-surface "%OUT_DIR%"
exit /b %errorlevel%
