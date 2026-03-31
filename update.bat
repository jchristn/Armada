@echo off
echo.
echo [update] Stopping Armada server if it is running...
armada server stop

echo.
echo [update] Reinstalling Armada tool and redeploying dashboard...
call "%~dp0reinstall.bat"
if errorlevel 1 exit /b 1

echo.
echo [update] Starting Armada server...
armada server start
