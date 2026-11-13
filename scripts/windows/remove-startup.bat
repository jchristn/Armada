@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Unregister Armada from starting at login (delete the current-user Run value "ArmadaAdmiral"), then stop every
echo(Armada.Server. Succeeds with "nothing to remove" when it is not registered. Fails when Armada is registered as the
echo(Windows service "armada" (remove that with Armada.Server.exe --uninstall-service). The published files stay.
echo(Usage: scripts\windows\remove-startup.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>]
echo(  The framework is accepted for parity with the other scripts and is not used.
exit /b 0
:armada_main

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg
set "STARTUP_VALUE_NAME=ArmadaAdmiral"
set "STARTUP_DISPLAY_NAME=Armada Admiral"
set "START_SCRIPT=%SCRIPT_DIR%\start-armada-server.ps1"
set "STOP_SCRIPT=%SCRIPT_DIR%\stop-armada-server.ps1"
set "SERVER_EXE=%USERPROFILE%\.armada\bin\Armada.Server.exe"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

where powershell >nul 2>nul
if errorlevel 1 (
    echo ERROR: powershell.exe was not found on PATH. 1>&2
    exit /b 1
)

rem PowerShell reads every path from the environment ($env:...), never from this command line, so a checkout path
rem with ^&, ^^, %% or quotes cannot break it. The two checks that run before publishing use "call" so a .cmd stand-in
rem (the Services.ReleaseVersion test) returns here; their command text holds no %%VAR%% or ^^, so the second parse
rem that call does changes nothing.

rem "Armada.Server.exe --install-service" registers the Admiral as the Windows service "armada", which runs its own
rem Admiral; two registrations would start two on the same ports (the same rule on every OS).
sc.exe query armada >nul 2>nul
if not errorlevel 1 goto :armada_product_registered

rem Exit code 3 means the Run value exists.
call powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $env:STARTUP_VALUE_NAME -ErrorAction SilentlyContinue) { exit 3 } else { exit 0 }"
if errorlevel 3 (set "ARMADA_REGISTERED=1") else (set "ARMADA_REGISTERED=")
if not defined ARMADA_REGISTERED (
    echo [remove-startup] Armada is not registered to start at login; nothing to remove.
    exit /b 0
)

echo.
echo [remove-startup] Unregistering Armada from starting at login...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $env:STARTUP_VALUE_NAME -ErrorAction Stop"
if errorlevel 1 goto :armada_register_failed

echo.
echo [remove-startup] Stopping every running Armada.Server process...
rem The PowerShell helper stops published and dotnet-hosted instances and waits for them to exit.
call powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:STOP_SCRIPT; exit $LASTEXITCODE"
if errorlevel 1 goto :armada_still_running

echo.
echo [remove-startup] Completed.
endlocal & exit /b 0

:armada_product_registered
echo ERROR: Armada is registered as the Windows service "armada" by "Armada.Server.exe --install-service", which runs its own Admiral. Remove that first from an elevated prompt with: "%USERPROFILE%\.armada\bin\Armada.Server.exe" --uninstall-service, then run this again. 1>&2
exit /b 1

:armada_still_running
echo ERROR: Armada.Server is still running and could not be stopped. 1>&2
exit /b 1

:armada_no_exe
echo ERROR: Published server executable not found at %SERVER_EXE% 1>&2
exit /b 1

:armada_register_failed
echo ERROR: Could not delete the Run registry value "%STARTUP_VALUE_NAME%". 1>&2
exit /b 1

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2
