@echo off
rem Work relative to this script (docker\armada\factory) so it is safe to run from any directory.
pushd "%~dp0" >nul
echo ========================================
echo  Armada Factory Reset
echo ========================================
echo.
echo This will delete local SQLite database files and log files.
echo Configuration (armada.json) will be preserved.
echo If docker\armada\armada.json points at an external database,
echo that external database will NOT be modified by this script.
echo.
set /p confirm="Type 'RESET' to confirm: "
if /i not "%confirm%"=="RESET" (
    echo Cancelled.
    popd >nul
    exit /b 1
)
echo.
echo [1/3] Stopping containers...
pushd .. >nul
docker compose down
popd >nul
echo.
echo [2/3] Deleting local database files...
if exist "..\db" (
    del /q "..\db\*" 2>nul
    echo   Local database files deleted.
) else (
    echo   No database directory found.
)
echo.
echo [3/3] Deleting log files...
if exist "..\logs" (
    del /q "..\logs\*" 2>nul
    echo   Log files deleted.
) else (
    echo   No logs directory found.
)
echo.
echo ========================================
echo  Factory reset complete.
echo  Run 'cd docker\armada ^&^& docker compose up -d' to restart.
echo ========================================
popd >nul
