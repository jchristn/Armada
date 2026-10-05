@echo off
rem ============================================================================
rem  build-installers.bat - build the Windows installers Armada can produce on
rem  this machine (CLI, Harbor, Server) and land them in installers\<version>\.
rem
rem  macOS (.dmg/.pkg) and Linux (.deb/.rpm) installers cannot be built
rem  on Windows; run build-installers.sh on those platforms, or push a tag to let
rem  the CI matrix build all three operating systems at once.
rem
rem  Usage: build-installers.bat <version>
rem  Example: build-installers.bat 1.0.0
rem ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" (
  echo Usage: build-installers.bat ^<version^>
  echo Example: build-installers.bat 1.0.0
  exit /b 1
)

set "PUBLISHER=src\Armada.Publisher\Armada.Publisher.csproj"
set "OUT=installers\%VERSION%"
set "WORK=%OUT%\_work"
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%WORK%" mkdir "%WORK%"

echo Building Armada.Publisher...
dotnet build "%PUBLISHER%" -c Release --nologo
if errorlevel 1 exit /b 1

set "BUILT="
set "FAILED="

rem --- channels this OS owns -------------------------------------------------
call :build inno-harbor
call :build wix-server
call :build nuget-cli

rem --- collect the finished installers into installers\<version>\ ------------
echo.
echo Collecting installers into %OUT% ...
if exist "%WORK%\packages" (
  for /r "%WORK%\packages" %%F in (*.exe *.msi *.nupkg) do copy /y "%%F" "%OUT%\" >nul
)

rem --- SHA256SUMS over everything collected ------------------------------------
dotnet run --project "%PUBLISHER%" -c Release --no-build -- checksums --dir "%OUT%"
if errorlevel 1 set "FAILED=!FAILED! checksums"

echo.
echo ============================================================
echo   Version: %VERSION%
echo   Built:  !BUILT!
echo   Failed: !FAILED!
echo   Output: %OUT%
echo ============================================================
if not "!FAILED!"=="" (
  echo Note: failed channels are either missing a packaging tool ^(run
  echo       "dotnet run --project %PUBLISHER% -- doctor"^) or hit a build error
  echo       shown in the channel output above.
)
endlocal
exit /b 0

rem --- subroutine: build one channel, record pass/fail, never abort ----------
:build
set "CH=%~1"
echo.
echo --- channel %CH% ---
dotnet run --project "%PUBLISHER%" -c Release --no-build -- --channel %CH% --version %VERSION% --output "%WORK%"
if errorlevel 1 (
  set "FAILED=!FAILED! %CH%"
) else (
  set "BUILT=!BUILT! %CH%"
)
exit /b 0
