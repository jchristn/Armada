@echo off
echo.
echo [update] Stopping Armada server if it is running...
armada server stop

echo.
echo [update] Stopping repo-backed Armada MCP stdio hosts if they are running...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$repoRoot = [System.IO.Path]::GetFullPath('%~dp0'); " ^
  "$targets = Get-CimInstance Win32_Process -Filter ""Name = 'dotnet.exe'"" | Where-Object { $_.CommandLine -like '*Armada.Helm.dll* mcp stdio*' -and $_.CommandLine -like ('*' + $repoRoot + '*') }; " ^
  "if (-not $targets) { Write-Host '[update] No repo-backed MCP stdio hosts found.'; exit 0 }; " ^
  "$targets | ForEach-Object { Write-Host ('[update] Stopping MCP stdio host PID ' + $_.ProcessId + '...'); Stop-Process -Id $_.ProcessId -Force }"
if errorlevel 1 exit /b 1

echo.
echo [update] Reinstalling Armada tool and redeploying dashboard...
call "%~dp0reinstall.bat"
if errorlevel 1 exit /b 1

echo.
echo [update] Starting Armada server...
armada server start
