@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Remove the Armada MCP server from Claude Code, Codex, Gemini, Cursor and (when present) Mux, OpenCode and pi.
echo(Usage: scripts\windows\remove-mcp.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
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
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

echo.
echo [remove-mcp] Removing Armada MCP for Claude Code, Codex, Gemini, and Cursor...
call dotnet run --project "%REPO_ROOT%\src\Armada.Helm" %ARMADA_DOTNET_FRAMEWORK_ARGS% -- mcp remove --yes
if errorlevel 1 exit /b 1

echo.
echo [remove-mcp] Completed.
exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2
