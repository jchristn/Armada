@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "DASHBOARD_DIR=%ROOT_DIR%src\Armada.Dashboard"
set "DIST_DIR=%DASHBOARD_DIR%\dist"
set "TARGET_DIR=%USERPROFILE%\.armada\dashboard"

echo.
echo [deploy-dashboard] Starting dashboard build and deploy

if not exist "%DASHBOARD_DIR%\package.json" (
    echo ERROR: Dashboard project not found at %DASHBOARD_DIR%
    exit /b 1
)

pushd "%DASHBOARD_DIR%"
if not exist "node_modules" (
    echo [deploy-dashboard] Installing dashboard dependencies...
    echo Installing dashboard dependencies...
    call npm.cmd install
    if errorlevel 1 (
        popd
        exit /b 1
    )
)

echo [deploy-dashboard] Building dashboard...
echo Building dashboard...
call npm.cmd run build
if errorlevel 1 (
    popd
    exit /b 1
)
popd

if not exist "%DIST_DIR%\index.html" (
    echo ERROR: Dashboard build did not produce dist\index.html
    exit /b 1
)

echo [deploy-dashboard] Deploying dashboard to %TARGET_DIR%
if exist "%TARGET_DIR%" rmdir /s /q "%TARGET_DIR%"
mkdir "%TARGET_DIR%" >nul 2>nul
xcopy "%DIST_DIR%\*" "%TARGET_DIR%\" /E /I /Y >nul
if errorlevel 1 (
    echo ERROR: Failed to deploy dashboard to %TARGET_DIR%
    exit /b 1
)

echo Dashboard deployed to %TARGET_DIR%
exit /b 0
