@echo off
setlocal

set "TAG=%~1"
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
