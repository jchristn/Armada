@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Wait up to 30 seconds for ^<base url^>/api/v1/status/health to answer.
echo(Usage: scripts\windows\healthcheck-server.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [^<base url^>]
echo(  ^<base url^>   default ARMADA_BASE_URL, else http://localhost:7890; a leading framework is ignored
exit /b 0
:armada_main

set "BASE_URL=%~1"
if /I "%~1"=="-f" set "BASE_URL=%~3"
if /I "%~1"=="--framework" set "BASE_URL=%~3"
if not "%~1"=="" (
    echo(%~1| findstr /R /I "^net[0-9][0-9]*\.[0-9][0-9]*$" >nul
    if not errorlevel 1 set "BASE_URL=%~2"
)
if "%BASE_URL%"=="" set "BASE_URL=%ARMADA_BASE_URL%"
if "%BASE_URL%"=="" set "BASE_URL=http://localhost:7890"
if "%BASE_URL:~-1%"=="/" set "BASE_URL=%BASE_URL:~0,-1%"
set "HEALTH_URL=%BASE_URL%/api/v1/status/health"

echo.
echo [healthcheck-server] Waiting for %HEALTH_URL%...

for /l %%I in (1,1,30) do (
    powershell -NoProfile -Command ^
        "try { $resp = Invoke-WebRequest -UseBasicParsing -Uri '%HEALTH_URL%' -TimeoutSec 3; if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>nul
    if not errorlevel 1 (
        echo [healthcheck-server] Healthy.
        exit /b 0
    )

    timeout /t 1 /nobreak >nul
)

echo ERROR: Armada server did not become healthy at %HEALTH_URL% 1>&2
exit /b 1
