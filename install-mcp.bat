@echo off
setlocal

echo.
echo [install-mcp] Configuring Armada MCP for Claude Code, Codex, Gemini, and Cursor...
dotnet run --project "%~dp0src\Armada.Helm" -f net8.0 -- mcp install --yes
if errorlevel 1 exit /b 1

echo.
echo [install-mcp] Completed.
exit /b 0
