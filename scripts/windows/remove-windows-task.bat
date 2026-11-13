@echo off
setlocal
rem Deprecated name, kept so existing docs, notes and habits keep working: remove-windows-task.bat is now
rem remove-startup.bat, the start-at-login script name used on every OS. This forwards all arguments to it;
rem run scripts\windows\remove-startup.bat -h for usage.
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
echo [remove-windows-task] Note: this script is now scripts\windows\remove-startup.bat; running that. 1>&2
call "%SCRIPT_DIR%\remove-startup.bat" %*
endlocal & exit /b %ERRORLEVEL%
