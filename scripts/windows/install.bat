@echo off
setlocal
if /I "%~1"=="-h" goto :armada_usage
if /I "%~1"=="--help" goto :armada_usage
if "%~1"=="/?" goto :armada_usage
goto :armada_main
:armada_usage
echo(Build Armada, deploy the dashboard and install the armada CLI (Armada.Helm) as a global .NET tool.
echo(Fails when Armada.Helm is already installed (use reinstall.bat) and while an Armada server is running.
echo(Usage: scripts\windows\install.bat [^<framework^> ^| -f ^<framework^> ^| --framework ^<framework^>] [-k^|--insecure]
echo(  ^<framework^>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
echo(  --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
exit /b 0
:armada_main

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
call "%SCRIPT_DIR%\resolve-framework.bat" %*
if errorlevel 1 exit /b 1
if defined ARMADA_UNEXPECTED_ARG goto :armada_bad_arg
for %%I in ("%SCRIPT_DIR%\..\..") do set "REPO_ROOT=%%~fI"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
    exit /b 1
)

echo.
echo [install] Checking for a running Armada server...
call :armada_check_running
if defined ARMADA_RUNNING_PID goto :armada_running_error
echo [install] Checking for an existing install...
dotnet tool list --global 2>nul | findstr /B /I /C:"armada.helm " >nul
if not errorlevel 1 (
    echo ERROR: Armada.Helm is already installed. Run scripts\windows\reinstall.bat to replace it. 1>&2
    exit /b 1
)

echo.
echo [install] Deploying dashboard...
call "%SCRIPT_DIR%\deploy-dashboard.bat"
if errorlevel 1 exit /b 1

echo.
echo [install] Building Armada solution for %ARMADA_TARGET_FRAMEWORK%...
call dotnet build "%REPO_ROOT%\src\Armada.sln" %ARMADA_DOTNET_FRAMEWORK_ARGS%
if errorlevel 1 exit /b 1

echo.
echo [install] Packing Armada.Helm for %ARMADA_TARGET_FRAMEWORK%...
call dotnet pack "%REPO_ROOT%\src\Armada.Helm\Armada.Helm.csproj" %ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS% -o "%REPO_ROOT%\src\nupkg"
if errorlevel 1 exit /b 1

echo.
echo [install] Installing Armada.Helm as a global tool...
call dotnet tool install --global --add-source "%REPO_ROOT%\src\nupkg" Armada.Helm
if errorlevel 1 exit /b 1

echo.
echo [install] Completed.
exit /b 0

:armada_bad_arg
echo ERROR: Unexpected argument: %ARMADA_UNEXPECTED_ARG% (run with -h for usage) 1>&2
exit /b 2

:armada_check_running
REM Sets ARMADA_RUNNING_PID to a running Admiral's PID (the published Armada.Server.exe or a dotnet host of
REM Armada.Server.dll), or clears it.
set "ARMADA_RUNNING_PID="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\stop-armada-server.ps1" -List`) do set "ARMADA_RUNNING_PID=%%P"
exit /b 0

:armada_running_error
echo ERROR: An Armada server is running (PID %ARMADA_RUNNING_PID%). Stop it with powershell -File scripts\windows\stop-armada-server.ps1 and try again. 1>&2
exit /b 1
