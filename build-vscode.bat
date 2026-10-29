@echo off
setlocal

rem Builds, tests and packages the Armada VS Code extension (src/Armada.VSCode) into a .vsix.
rem The VSIX version comes from src/Armada.VSCode/package.json.

if not "%~1"=="" (
    echo Usage: build-vscode.bat
    echo Builds src\Armada.VSCode\armada-vscode-^<version^>.vsix ^(version from its package.json^).
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

echo Running the extension unit tests...
call npm test
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Packaging the VSIX...
call npm run package
set "EXIT_CODE=%ERRORLEVEL%"

:done
if "%EXIT_CODE%"=="0" echo Done. VSIX written to src\Armada.VSCode.
popd
endlocal & exit /b %EXIT_CODE%
