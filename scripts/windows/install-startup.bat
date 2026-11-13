@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Publish Armada.Server into %%USERPROFILE%%\.armada\bin, register it to start at login with the current-user
echo(Run registry value "ArmadaAdmiral" (HKCU, no elevation), start it now and wait for its health check.
echo(Stops any running Armada server first. Fails when Armada is already registered (use update-startup.bat)
echo(or registered as the Windows service "armada" by Armada.Server.exe --install-service.
echo(Usage: scripts\windows\install-startup.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
echo(  ^<framework^>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
echo(  --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
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
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)
if defined ARMADA_REGISTERED (
    echo ERROR: Armada is already registered to start at login. Run scripts\windows\update-startup.bat to republish and restart it. 1>&2
    exit /b 1
)

echo.
echo [install-startup] Stopping every running Armada.Server process...
rem The PowerShell helper stops published and dotnet-hosted instances and waits for them to exit.
call powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:STOP_SCRIPT; exit $LASTEXITCODE"
if errorlevel 1 goto :armada_still_running

echo.
echo [install-startup] Using target framework %ARMADA_TARGET_FRAMEWORK%...
echo [install-startup] Publishing Armada.Server...
call "%SCRIPT_DIR%\publish-server.bat" %ARMADA_FORWARD_FRAMEWORK_ARGS%
if errorlevel 1 exit /b 1
if not exist "%SERVER_EXE%" goto :armada_no_exe

echo.
echo [install-startup] Registering current-user startup entry "%STARTUP_DISPLAY_NAME%"...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$startupPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';" ^
    "$powerShellExe = $env:POWERSHELL_EXE;" ^
    "if (-not (Test-Path -LiteralPath $powerShellExe)) { $powerShellExe = 'powershell.exe' };" ^
    "$startScript = [System.IO.Path]::GetFullPath($env:START_SCRIPT);" ^
    "$quote = [char]34;" ^
    "$command = $quote + $powerShellExe + $quote + ' -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ' + $quote + $startScript + $quote;" ^
    "New-Item -Path $startupPath -Force | Out-Null;" ^
    "New-ItemProperty -Path $startupPath -Name $env:STARTUP_VALUE_NAME -Value $command -PropertyType String -Force | Out-Null"
if errorlevel 1 goto :armada_register_failed

echo.
echo [install-startup] Starting Armada.Server in the current user session...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& $env:START_SCRIPT; exit $LASTEXITCODE"
if errorlevel 1 exit /b 1

call "%SCRIPT_DIR%\healthcheck-server.bat"
if errorlevel 1 exit /b 1

echo.
echo [install-startup] Completed.
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
echo ERROR: Could not write the Run registry value "%STARTUP_VALUE_NAME%". 1>&2
exit /b 1

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2
