@echo off
setlocal enabledelayedexpansion

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

where docker >nul 2>&1
if errorlevel 1 (
    echo [e2e-split] ERROR: docker is not installed or not on PATH.
    exit /b 1
)

echo [e2e-split] Building server image (%IMAGE_TAG%)...
docker build -t "%IMAGE_TAG%" -f "%REPO_ROOT%\src\Armada.Server\Dockerfile" "%REPO_ROOT%"
if errorlevel 1 exit /b 1

docker rm -f "%CONTAINER_NAME%" >nul 2>&1

echo [e2e-split] Booting ephemeral Admiral (split mode)...
docker run -d --rm --name "%CONTAINER_NAME%" -p 7890:7890 -p 7891:7891 -p 9464:9464 -v "%REPO_ROOT%\docker\armada\armada.split.json:/app/data/armada.json:ro" "%IMAGE_TAG%" >nul
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
    echo [e2e-split] ERROR: Admiral did not become healthy within 60s. Logs:
    docker logs "%CONTAINER_NAME%" 2>&1
    if not "%ARMADA_E2E_KEEP%"=="1" docker rm -f "%CONTAINER_NAME%" >nul 2>&1
    exit /b 1
)

if "%ARMADA_E2E_KEEP%"=="1" (
    echo [e2e-split] ARMADA_E2E_KEEP=1 -- leaving container '%CONTAINER_NAME%' running for manual Harbor attach.
) else (
    echo [e2e-split] Tearing down ephemeral container...
    docker rm -f "%CONTAINER_NAME%" >nul 2>&1
)

echo [e2e-split] Split-mode ephemeral smoke test complete.
endlocal
