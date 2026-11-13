@echo off
setlocal
rem Deprecated name, kept so existing docs, notes and habits keep working: update-windows-task.bat is now
rem update-startup.bat, the start-at-login script name used on every OS. This forwards all arguments to it;
rem run scripts\windows\update-startup.bat -h for usage.
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
echo [update-windows-task] Note: this script is now scripts\windows\update-startup.bat; running that. 1>&2
call "%SCRIPT_DIR%\update-startup.bat" %*
endlocal & exit /b %ERRORLEVEL%
