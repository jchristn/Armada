@echo off
setlocal enabledelayedexpansion

echo.
echo Armada Settings Migration: v0.1.0 to v0.2.0
echo =============================================
echo.

REM Determine settings file path
if "%~1"=="" (
    set "SETTINGS_FILE=%USERPROFILE%\.armada\settings.json"
) else (
    set "SETTINGS_FILE=%~1"
)

echo Settings file: %SETTINGS_FILE%

REM Check if the file exists
if not exist "%SETTINGS_FILE%" (
    echo.
    echo ERROR: Settings file not found: %SETTINGS_FILE%
    echo.
    echo Usage: %~nx0 [path\to\settings.json]
    echo Default path: %USERPROFILE%\.armada\settings.json
    exit /b 1
)

REM Use PowerShell for JSON manipulation
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$settingsPath = '%SETTINGS_FILE%';" ^
    "$backupPath = $settingsPath + '.v0.1.0.bak';" ^
    "" ^
    "try {" ^
    "    $content = Get-Content -Path $settingsPath -Raw -ErrorAction Stop;" ^
    "    $json = $content | ConvertFrom-Json -ErrorAction Stop;" ^
    "} catch {" ^
    "    Write-Host '';" ^
    "    Write-Host 'ERROR: Failed to read or parse settings.json. The file may contain invalid JSON.';" ^
    "    Write-Host \"Details: $($_.Exception.Message)\";" ^
    "    exit 1;" ^
    "}" ^
    "" ^
    "if (-not ($json.PSObject.Properties.Name -contains 'databasePath')) {" ^
    "    Write-Host '';" ^
    "    Write-Host 'WARNING: No databasePath property found in settings.json.';" ^
    "    Write-Host 'The file may have already been migrated to v0.2.0 format.';" ^
    "    exit 0;" ^
    "}" ^
    "" ^
    "$dbPath = $json.databasePath;" ^
    "" ^
    "Copy-Item -Path $settingsPath -Destination $backupPath -Force;" ^
    "Write-Host \"Backup created: $backupPath\";" ^
    "" ^
    "$json.PSObject.Properties.Remove('databasePath');" ^
    "" ^
    "$database = [PSCustomObject]@{" ^
    "    type = 'Sqlite';" ^
    "    filename = $dbPath;" ^
    "    hostname = 'localhost';" ^
    "    port = 0;" ^
    "    username = '';" ^
    "    password = '';" ^
    "    databaseName = '';" ^
    "    schema = '';" ^
    "    requireEncryption = $false;" ^
    "    logQueries = $false;" ^
    "    minPoolSize = 1;" ^
    "    maxPoolSize = 25;" ^
    "    connectionLifetimeSeconds = 300;" ^
    "    connectionIdleTimeoutSeconds = 60;" ^
    "};" ^
    "" ^
    "$json | Add-Member -NotePropertyName 'database' -NotePropertyValue $database;" ^
    "" ^
    "$json | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsPath -Encoding UTF8;" ^
    "" ^
    "Write-Host '';" ^
    "Write-Host 'Migration complete!';" ^
    "Write-Host \"  Original (backed up): $backupPath\";" ^
    "Write-Host \"  Updated: $settingsPath\";" ^
    "Write-Host '';" ^
    "Write-Host 'Changes:';" ^
    "Write-Host \"  - Removed 'databasePath': $dbPath\";" ^
    "Write-Host '  - Added ''database'' object with connection pooling settings';"

exit /b %ERRORLEVEL%
