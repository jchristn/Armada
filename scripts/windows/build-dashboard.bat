@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Build the jchristn77/armada-dashboard image (linux/amd64, linux/arm64/v8) on the cloud builder, push it
echo(as :latest (and :^<tag^>), then pull the pushed tags into the local image store.
echo(Usage: scripts\windows\build-dashboard.bat [^<tag^>]
exit /b 0
:armada_main

set "TAG=%~1"
if not "%~2"=="" goto :armada_bad_tag
if "%TAG:~0,1%"=="-" goto :armada_bad_tag
where docker >nul 2>nul
if errorlevel 1 (
    echo ERROR: docker was not found on PATH. Install Docker Desktop: https://docs.docker.com/get-docker/ 1>&2
    exit /b 1
)
docker info >nul 2>nul
if errorlevel 1 (
    echo ERROR: The Docker engine is not reachable. Start Docker Desktop and try again. 1>&2
    exit /b 1
)
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
set "IMAGE=jchristn77/armada-dashboard"

pushd "%REPO_ROOT%" >nul
if errorlevel 1 exit /b 1

rem Build once on the cloud builder and push the multi-arch manifest to Docker Hub.
if "%TAG%"=="" (
    echo Building %IMAGE%:latest
    docker buildx build ^
        --builder cloud-jchristn77-jchristn77 ^
        --platform linux/amd64,linux/arm64/v8 ^
        -f src/Armada.Dashboard/Dockerfile ^
        -t %IMAGE%:latest ^
        --push ^
        .
) else (
    echo Building %IMAGE%:latest and %IMAGE%:%TAG%
    docker buildx build ^
        --builder cloud-jchristn77-jchristn77 ^
        --platform linux/amd64,linux/arm64/v8 ^
        -f src/Armada.Dashboard/Dockerfile ^
        -t %IMAGE%:latest ^
        -t %IMAGE%:%TAG% ^
        --push ^
        .
)

set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" goto :done

rem Pull the pushed image back into the local registry (from Docker Hub, not the
rem cloud builder) so the same tags are available locally as well.
echo Pulling %IMAGE%:latest into local registry
docker pull %IMAGE%:latest
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" goto :done

if "%TAG%"=="" goto :done
echo Pulling %IMAGE%:%TAG% into local registry
docker pull %IMAGE%:%TAG%
set "EXITCODE=%ERRORLEVEL%"

:done
popd >nul
exit /b %EXITCODE%

:armada_bad_tag
echo ERROR: Unexpected argument (run with -h for usage). This script takes one optional image tag. 1>&2
exit /b 2
