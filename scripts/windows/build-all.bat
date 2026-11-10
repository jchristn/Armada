@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Build, push and pull the armada-server, armada-dashboard and armada-proxy Docker images in turn.
echo(Usage: scripts\windows\build-all.bat [^<tag^>]
exit /b 0
:armada_main

set "TAG=%~1"
if not "%~2"=="" goto :armada_bad_tag
if "%TAG:~0,1%"=="-" goto :armada_bad_tag
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

echo === Building Armada server ===
call "%SCRIPT_DIR%\build-server.bat" %1
if errorlevel 1 exit /b 1

echo === Building Armada dashboard ===
call "%SCRIPT_DIR%\build-dashboard.bat" %1
if errorlevel 1 exit /b 1

echo === Building Armada proxy ===
call "%SCRIPT_DIR%\build-proxy.bat" %1
if errorlevel 1 exit /b 1

echo === All Armada images built, pushed to Docker Hub, and pulled into the local registry ===
exit /b 0

:armada_bad_tag
echo ERROR: Unexpected argument (run with -h for usage). This script takes one optional image tag. 1>&2
exit /b 2
