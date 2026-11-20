@echo off
setlocal

rem Builds, tests and packages the Armada VS Code extension (src\Armada.VSCode) into
rem vsix\armada-vscode-<version>.vsix (version from its package.json). With --install, also installs it into VS Code.

set "INSTALL=0"
if "%~1"=="--install" (
    set "INSTALL=1"
) else if not "%~1"=="" (
    echo Usage: build-vscode.bat [--install]
    echo Builds vsix\armada-vscode-^<version^>.vsix; --install also installs it into VS Code.
    endlocal
    exit /b 1
)

pushd "%~dp0src\Armada.VSCode"

echo Installing extension dependencies...
call npm ci
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Building the extension...
call npm run build
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Linting the extension and the dashboard modules it shares...
call npm run lint
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Running the extension unit tests...
call npm test
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Packaging the VSIX...
call npm run package
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

for /f "delims=" %%v in ('node -p "require('./package.json').version"') do set "VERSION=%%v"
set "VSIX=%~dp0vsix\armada-vscode-%VERSION%.vsix"
echo Done. VSIX: %VSIX%

if not "%INSTALL%"=="1" goto :done

where code >nul 2>nul
if errorlevel 1 goto :nocode
call code --install-extension "%VSIX%" --force
set "EXIT_CODE=%ERRORLEVEL%"
if "%EXIT_CODE%"=="0" echo Installed. Reload any open VS Code windows (Developer: Reload Window).
goto :done

:nocode
echo The VS Code 'code' command was not found; install %VSIX% from the Extensions view instead.
set "EXIT_CODE=1"

:done
popd
endlocal & exit /b %EXIT_CODE%
