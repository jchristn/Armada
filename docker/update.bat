@echo off
rem ============================================================================
rem  update.bat - pull the latest Armada images and recreate the stack.
rem
rem  Non-destructive: named volumes and the bind-mounted db\ and logs\ folders are
rem  preserved. For a destructive reset use docker\armada\factory\reset.bat.
rem
rem  Usage:   update.bat [compose-file]
rem  Default: docker\armada\compose.yaml
rem  Example: update.bat armada\compose.split.yaml
rem           update.bat proxy\compose.yaml
rem ============================================================================
setlocal
set "SCRIPT_DIR=%~dp0"
set "COMPOSE_FILE=%~1"
if "%COMPOSE_FILE%"=="" set "COMPOSE_FILE=armada\compose.yaml"
if not exist "%COMPOSE_FILE%" set "COMPOSE_FILE=%SCRIPT_DIR%%COMPOSE_FILE%"

if not exist "%COMPOSE_FILE%" (
  echo Compose file not found: %COMPOSE_FILE%
  exit /b 1
)

echo ========================================
echo  Armada Update
echo  Compose file: %COMPOSE_FILE%
echo ========================================

echo.
echo [1/4] Pulling latest images...
rem Services that are built from source (build:) have no published image to pull; skip them
rem instead of failing, then rebuild them on the way back up.
docker compose -f "%COMPOSE_FILE%" pull --ignore-buildable
if errorlevel 1 exit /b 1

echo.
echo [2/4] Stopping the stack (volumes are preserved)...
docker compose -f "%COMPOSE_FILE%" down
if errorlevel 1 exit /b 1

echo.
echo [3/4] Starting the stack...
docker compose -f "%COMPOSE_FILE%" up -d --build
if errorlevel 1 exit /b 1

echo.
echo [4/4] Container status:
docker ps -a

echo.
echo Update complete.
endlocal
exit /b 0
