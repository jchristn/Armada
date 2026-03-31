@echo off
setlocal

echo.
echo [reinstall] Removing existing global Armada.Helm tool if present...
dotnet tool uninstall --global Armada.Helm >nul 2>nul

echo.
echo [reinstall] Running fresh install...
call "%~dp0install.bat"
exit /b %ERRORLEVEL%
