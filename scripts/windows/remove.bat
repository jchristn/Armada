@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Uninstall the armada CLI and delete the deployed dashboard; succeeds with "nothing to remove" when neither is there.
echo(Fails while an Armada server is running.
echo(Usage: scripts\windows\remove.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>]
echo(  The framework is accepted for parity with the other scripts and is not used.
exit /b 0
:armada_main

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

echo.
echo [remove] Checking for a running Armada server...
call :armada_check_running
if defined ARMADA_RUNNING_PID goto :armada_running_error

set "REMOVED=0"
echo.
echo [remove] Uninstalling the global Armada.Helm tool...
dotnet tool list --global 2>nul | findstr /B /I /C:"armada.helm " >nul
if errorlevel 1 (
    echo [remove] Armada.Helm is not installed.
) else (
    call dotnet tool uninstall --global Armada.Helm
    if errorlevel 1 exit /b 1
    set "REMOVED=1"
)

if not exist "%USERPROFILE%\.armada\dashboard" goto :armada_dashboard_done
echo.
echo [remove] Removing deployed dashboard from %USERPROFILE%\.armada\dashboard
rmdir /s /q "%USERPROFILE%\.armada\dashboard"
if exist "%USERPROFILE%\.armada\dashboard" (
    echo ERROR: Could not delete the deployed dashboard; a running process may hold it open. 1>&2
    exit /b 1
)
set "REMOVED=1"
:armada_dashboard_done

echo.
if "%REMOVED%"=="0" (
    echo [remove] Armada is not installed; nothing to remove.
) else (
    echo [remove] Completed.
)
exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2

:armada_check_running
REM Sets ARMADA_RUNNING_PID to a running Admiral's PID (the published Armada.Server.exe or a dotnet host of
REM Armada.Server.dll), or clears it.
set "ARMADA_RUNNING_PID="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\stop-armada-server.ps1" -List`) do set "ARMADA_RUNNING_PID=%%P"
exit /b 0

:armada_running_error
echo ERROR: An Armada server is running (PID %ARMADA_RUNNING_PID%). Stop it with powershell -File scripts\windows\stop-armada-server.ps1 and try again. 1>&2
exit /b 1
