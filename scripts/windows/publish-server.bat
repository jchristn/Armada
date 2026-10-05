@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
set "PUBLISH_DIR=%USERPROFILE%\.armada\bin"
set "SERVER_EXE=%PUBLISH_DIR%\Armada.Server.exe"

echo.
echo [publish-server] Publishing Armada.Server for %ARMADA_TARGET_FRAMEWORK% to %PUBLISH_DIR%...
call dotnet publish "%REPO_ROOT%\src\Armada.Server" -c Release %ARMADA_DOTNET_FRAMEWORK_ARGS% -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b 1

echo.
echo [publish-server] Deploying dashboard assets...
call "%SCRIPT_DIR%\deploy-dashboard.bat"
if errorlevel 1 (
    if exist "%USERPROFILE%\.armada\dashboard\index.html" (
        echo [publish-server] WARNING: Dashboard deploy failed. Keeping the previously deployed React dashboard.
    ) else (
        echo ERROR: Dashboard deploy failed and no deployed React dashboard is available.
        exit /b 1
    )
)

if not exist "%SERVER_EXE%" (
    echo ERROR: Published server executable not found at %SERVER_EXE%
    exit /b 1
)

echo.
echo [publish-server] Completed.
echo [publish-server] Server executable: %SERVER_EXE%
exit /b 0
