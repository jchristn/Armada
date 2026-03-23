@echo off
setlocal

echo.
echo [remove-mcp] Removing Armada MCP for Claude Code, Codex, Gemini, and Cursor...
dotnet run --project "%~dp0src\Armada.Helm" -f net8.0 -- mcp remove --yes
if errorlevel 1 exit /b 1

echo.
echo [remove-mcp] Completed.
exit /b 0
