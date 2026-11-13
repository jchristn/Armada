@echo off
rem No delayed expansion: it would strip "!" from ARMADA_INITIAL_ADMIN_PASSWORD.
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Split-mode smoke test: build the server image, boot a throwaway container, check its health and MCP ports,
echo(then tear it down. Set ARMADA_E2E_KEEP=1 to leave the container running.
echo(Usage: scripts\windows\e2e-split-mode.bat
exit /b 0
:armada_main

REM Ephemeral-image split-mode smoke test (Windows). Builds the Armada Server image fresh, boots it as a
REM throwaway container in split-mode config, waits for the unauthenticated health endpoint, asserts the
REM containerized Admiral is serving, then tears it down. See scripts/common/e2e-split-mode.sh for details.
REM
REM Usage:
REM   e2e-split-mode.bat            build image, boot ephemeral container, health-check, tear down
REM   set ARMADA_E2E_KEEP=1 & e2e-split-mode.bat   leave the container running for manual Harbor attach

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"

set "IMAGE_TAG=armada-server:e2e-split"
set "CONTAINER_NAME=armada-e2e-split"
set "HEALTH_URL=http://127.0.0.1:7890/api/v1/status/health"
set "MCP_URL=http://127.0.0.1:7891/mcp"
REM The container listens on 0.0.0.0, so the Admiral refuses to start with the default admin password.
if defined ARMADA_INITIAL_ADMIN_PASSWORD (set "ADMIN_PASSWORD=%ARMADA_INITIAL_ADMIN_PASSWORD%") else (set "ADMIN_PASSWORD=E2e-Split-%RANDOM%%RANDOM%-Pw1")

if not "%~1"=="" goto :armada_bad_arg
where docker >nul 2>&1
if errorlevel 1 (
    echo [e2e-split] ERROR: docker is not installed or not on PATH. 1>&2
    exit /b 1
)

where curl >nul 2>&1
if errorlevel 1 (
    echo [e2e-split] ERROR: curl is not on PATH ^(Windows 10 1803 and later include curl.exe^). 1>&2
    exit /b 1
)
docker info >nul 2>&1
if errorlevel 1 (
    echo [e2e-split] ERROR: the Docker engine is not reachable. Start Docker Desktop and try again. 1>&2
    exit /b 1
)

echo [e2e-split] Building server image (%IMAGE_TAG%)...
docker build -t "%IMAGE_TAG%" -f "%REPO_ROOT%\src\Armada.Server\Dockerfile" "%REPO_ROOT%"
if errorlevel 1 exit /b 1

docker rm -f "%CONTAINER_NAME%" >nul 2>&1

echo [e2e-split] Booting ephemeral Admiral (split mode)...
docker run -d --rm --name "%CONTAINER_NAME%" -p 7890:7890 -p 7891:7891 -p 9464:9464 -e "ARMADA_INITIAL_ADMIN_PASSWORD=%ADMIN_PASSWORD%" -v "%REPO_ROOT%\docker\armada\armada.split.json:/app/data/settings.json:ro" "%IMAGE_TAG%" >nul
if errorlevel 1 exit /b 1

echo [e2e-split] Waiting for Admiral health at %HEALTH_URL% ...
set "HEALTHY=0"
for /L %%i in (1,1,60) do (
    curl -sf -o nul "%HEALTH_URL%" >nul 2>&1
    if not errorlevel 1 (
        set "HEALTHY=1"
        goto :healthy
    )
    timeout /t 1 /nobreak >nul
)

:healthy
if "%HEALTHY%"=="1" (
    echo [e2e-split] PASS: containerized Admiral is healthy.
) else (
    echo [e2e-split] ERROR: Admiral did not become healthy within 60s. Logs: 1>&2
    docker logs "%CONTAINER_NAME%" 2>&1
    if not "%ARMADA_E2E_KEEP%"=="1" docker rm -f "%CONTAINER_NAME%" >nul 2>&1
    exit /b 1
)

REM The MCP port must accept connections so a host Harbor's captains can call home. An unauthenticated probe is
REM expected to be rejected at the JSON-RPC layer; any HTTP status (not 000, connection refused) means it is served.
echo [e2e-split] Probing MCP endpoint at %MCP_URL%...
set "MCP_STATUS=000"
for /f %%C in ('curl -s -o nul -w "%%{http_code}" "%MCP_URL%" 2^>nul') do set "MCP_STATUS=%%C"
if "%MCP_STATUS%"=="000" (
    echo [e2e-split] WARNING: MCP endpoint did not answer; a host Harbor's captains could not call home. 1>&2
) else (
    echo [e2e-split] PASS: MCP endpoint is reachable from the host.
)

if "%ARMADA_E2E_KEEP%"=="1" (
    echo [e2e-split] ARMADA_E2E_KEEP=1 -- leaving container '%CONTAINER_NAME%' running for manual Harbor attach.
) else (
    echo [e2e-split] Tearing down ephemeral container...
    docker rm -f "%CONTAINER_NAME%" >nul 2>&1
)

echo [e2e-split] Split-mode ephemeral smoke test complete.
endlocal & exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %1 (this script takes no arguments; run with -h for usage) 1>&2
exit /b 2
