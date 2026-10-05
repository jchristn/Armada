@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-all.bat ^<tag^>
    echo Example: build-all.bat v1.0.0
    endlocal
    exit /b 1
)

set "TAG=%~1"

pushd "%~dp0"

echo === Building Armada Admiral image ===
call "%~dp0build-admiral.bat" "%TAG%"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo === Building Armada proxy image ===
call "%~dp0build-proxy.bat" "%TAG%"
set "EXIT_CODE=%ERRORLEVEL%"

:done
if "%EXIT_CODE%"=="0" echo === All images built for %TAG% and latest, pushed to Docker Hub, and pulled locally ===
popd
endlocal & exit /b %EXIT_CODE%
