@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem ====================================================================
rem Armada factory reset (Windows)
rem
rem Stops Armada processes, wipes ~/.armada runtime state (db, logs,
rem docks, repos, settings), and copies the gold settings.json from
rem this directory back into place. Requires typing RESET to proceed.
rem ====================================================================

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "TARGET_DIR=%USERPROFILE%\.armada"
set "GOLD_SETTINGS=%SCRIPT_DIR%\settings.json"

echo.
echo ========================================================
echo   ARMADA FACTORY RESET
echo ========================================================
echo.
echo This will:
echo   1. Stop any running Armada processes (Armada.Server, Armada.Helm).
echo   2. Delete the database, logs, docks, repos, and settings in:
echo        %TARGET_DIR%
echo   3. Replace settings.json with the factory gold copy from:
echo        %GOLD_SETTINGS%
echo.
echo This action cannot be undone. All missions, voyages, captains,
echo vessels, and local worktrees tracked by Armada will be removed.
echo.
set /p CONFIRM="Type RESET (in capitals) to proceed: "
if /I not "%CONFIRM%"=="RESET" (
    echo Aborted. No changes were made.
    endlocal
    exit /b 1
)

echo.
echo [1/3] Stopping Armada processes...
taskkill /F /IM Armada.Server.exe >nul 2>&1
taskkill /F /IM Armada.Helm.exe >nul 2>&1
rem Give the OS a moment to release file locks on the SQLite db.
ping -n 2 127.0.0.1 >nul

echo [2/3] Removing runtime data from %TARGET_DIR% ...
if exist "%TARGET_DIR%\logs"       rmdir /S /Q "%TARGET_DIR%\logs"
if exist "%TARGET_DIR%\docks"      rmdir /S /Q "%TARGET_DIR%\docks"
if exist "%TARGET_DIR%\repos"      rmdir /S /Q "%TARGET_DIR%\repos"
if exist "%TARGET_DIR%\armada.db"  del /F /Q  "%TARGET_DIR%\armada.db"
if exist "%TARGET_DIR%\armada.db-wal" del /F /Q "%TARGET_DIR%\armada.db-wal"
if exist "%TARGET_DIR%\armada.db-shm" del /F /Q "%TARGET_DIR%\armada.db-shm"
if exist "%TARGET_DIR%\settings.json" del /F /Q "%TARGET_DIR%\settings.json"

echo [3/3] Restoring gold settings.json ...
if not exist "%GOLD_SETTINGS%" (
    echo ERROR: factory gold file not found at %GOLD_SETTINGS%
    endlocal
    exit /b 2
)
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
copy /Y "%GOLD_SETTINGS%" "%TARGET_DIR%\settings.json" >nul

echo.
echo ========================================================
echo   Reset complete.
echo ========================================================
echo.
echo Restart Armada to rebuild the database from defaults:
echo.
echo   cd ..
echo   armada server start
echo.
endlocal
exit /b 0
