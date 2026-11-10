@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Build the React dashboard and deploy it into %%USERPROFILE%%\.armada\dashboard. Without Node.js it deploys
echo(the dist\ an earlier build left, and fails when there is none.
echo(Usage: scripts\windows\deploy-dashboard.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
echo(  The framework is accepted (so callers can forward their arguments) and is not used.
exit /b 0
:armada_main

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
set "DASHBOARD_DIR=%REPO_ROOT%\src\Armada.Dashboard"
set "DIST_DIR=%DASHBOARD_DIR%\dist"
set "TARGET_DIR=%USERPROFILE%\.armada\dashboard"
set "STAGING_DIR=%TARGET_DIR%.staging"
set "LOCAL_BIN_DIR=%DASHBOARD_DIR%\node_modules\.bin"
set "LOCAL_TSC=%LOCAL_BIN_DIR%\tsc.cmd"
set "LOCAL_VITE=%LOCAL_BIN_DIR%\vite.cmd"

REM Honor an "ignore TLS certificate" flag (-k / --insecure) for npm behind an SSL-inspecting proxy; a framework
REM argument is accepted (so callers can forward theirs) and not used.
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg

echo.
echo [deploy-dashboard] Starting dashboard build and deploy

REM Messages that print a path stay outside ( ) blocks: a ")" in the path (C:\Users\Bob (Work)) would end the block.
if not exist "%DASHBOARD_DIR%\package.json" goto :armada_no_project

REM Building the dashboard requires Node.js. When Node is unavailable, fall back to the pre-built
REM dist left by an earlier build (dist\ is not committed) so install still works without Node.
where node >nul 2>nul
if not errorlevel 1 goto :armada_have_node
if not exist "%DIST_DIR%\index.html" goto :armada_no_node
echo [deploy-dashboard] Node.js not found on PATH; deploying the pre-built dashboard from %DIST_DIR%
goto :deploy
:armada_have_node

pushd "%DASHBOARD_DIR%"
if not exist "node_modules" (
    echo [deploy-dashboard] Installing dependencies...
    call npm.cmd install
    if errorlevel 1 (
        popd
        exit /b 1
    )
)

echo [deploy-dashboard] Building with local dashboard toolchain...
call :build_dashboard
if errorlevel 1 (
    if exist "package-lock.json" (
        echo [deploy-dashboard] Initial build failed. Reinstalling dashboard dependencies and retrying...
        if exist "node_modules" rmdir /s /q "node_modules"
        call npm.cmd ci
        if errorlevel 1 (
            popd
            exit /b 1
        )

        echo [deploy-dashboard] Retrying build after clean install with local dashboard toolchain...
        call :build_dashboard
    )

    if errorlevel 1 (
        popd
        exit /b 1
    )
)
popd

if not exist "%DIST_DIR%\index.html" (
    echo ERROR: Dashboard build did not produce dist\index.html 1>&2
    exit /b 1
)

:deploy
echo [deploy-dashboard] Deploying dashboard to %TARGET_DIR%
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
mkdir "%STAGING_DIR%" >nul 2>nul
xcopy "%DIST_DIR%\*" "%STAGING_DIR%\" /E /I /Y >nul
if errorlevel 1 goto :armada_stage_failed

if exist "%TARGET_DIR%" rmdir /s /q "%TARGET_DIR%"
move "%STAGING_DIR%" "%TARGET_DIR%" >nul
if errorlevel 1 goto :armada_move_failed

echo Dashboard deployed to %TARGET_DIR%
exit /b 0

:build_dashboard
if not exist "%LOCAL_TSC%" goto :armada_no_tsc
if not exist "%LOCAL_VITE%" goto :armada_no_vite

call "%LOCAL_TSC%"
if errorlevel 1 exit /b 1

call "%LOCAL_VITE%" build
exit /b %ERRORLEVEL%

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2

:armada_no_project
echo ERROR: Dashboard project not found at %DASHBOARD_DIR% 1>&2
exit /b 1

:armada_no_node
echo ERROR: Node.js is not installed and no pre-built dashboard exists at %DIST_DIR%. 1>&2
echo Install Node.js ^(https://nodejs.org^) or build the dashboard once on a machine that has Node. 1>&2
exit /b 1

:armada_stage_failed
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
echo ERROR: Failed to stage dashboard for deployment to %TARGET_DIR% 1>&2
exit /b 1

:armada_move_failed
if exist "%STAGING_DIR%" rmdir /s /q "%STAGING_DIR%"
echo ERROR: Failed to deploy dashboard to %TARGET_DIR% 1>&2
exit /b 1

:armada_no_tsc
echo ERROR: Dashboard TypeScript compiler not found at %LOCAL_TSC% 1>&2
exit /b 1

:armada_no_vite
echo ERROR: Dashboard Vite CLI not found at %LOCAL_VITE% 1>&2
exit /b 1
