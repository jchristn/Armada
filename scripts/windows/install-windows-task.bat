@echo off
setlocal
rem Deprecated name, kept so existing docs, notes and habits keep working: install-windows-task.bat is now
rem install-startup.bat, the start-at-login script name used on every OS. The old script could be run again to
rem republish and restart, so this forwards to update-startup.bat when the Run value "ArmadaAdmiral" exists and to
rem install-startup.bat otherwise, with all arguments; run those with -h for usage.
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmadaAdmiral >nul 2>nul
if errorlevel 1 goto :armada_install
echo [install-windows-task] Note: this script is now scripts\windows\install-startup.bat; Armada is registered, so running update-startup.bat. 1>&2
call "%SCRIPT_DIR%\update-startup.bat" %*
endlocal & exit /b %ERRORLEVEL%
:armada_install
echo [install-windows-task] Note: this script is now scripts\windows\install-startup.bat; running that. 1>&2
call "%SCRIPT_DIR%\install-startup.bat" %*
endlocal & exit /b %ERRORLEVEL%
