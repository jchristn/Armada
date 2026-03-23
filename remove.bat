@echo off
setlocal

echo.
echo [remove] Uninstalling global Armada.Helm tool if present...
dotnet tool uninstall --global Armada.Helm >nul 2>nul

if exist "%USERPROFILE%\.armada\dashboard" (
    echo.
    echo [remove] Removing deployed dashboard from %USERPROFILE%\.armada\dashboard
    rmdir /s /q "%USERPROFILE%\.armada\dashboard"
)

echo.
echo [remove] Completed.
exit /b 0
