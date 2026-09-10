@echo off
setlocal

set "TAG=%~1"
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

echo === Building Armada server ===
call "%SCRIPT_DIR%\build-server.bat" %TAG%
if errorlevel 1 exit /b 1

echo === Building Armada dashboard ===
call "%SCRIPT_DIR%\build-dashboard.bat" %TAG%
if errorlevel 1 exit /b 1

echo === Building Armada proxy ===
call "%SCRIPT_DIR%\build-proxy.bat" %TAG%
if errorlevel 1 exit /b 1

echo === All Armada images built, pushed to Docker Hub, and pulled into the local registry ===
exit /b 0
