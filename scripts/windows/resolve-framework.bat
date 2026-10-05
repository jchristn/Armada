@echo off
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

REM Only a leading "net*" token counts as a bare framework (matching resolve-framework.sh), so a first
REM argument such as --insecure is not mistaken for the framework.
set "ARMADA_FW_ARG1=%~1"
if not defined ARMADA_FW_ARG1 set "ARMADA_FW_ARG1=-"

if /I "%~1"=="-f" (
    if "%~2"=="" (
        echo ERROR: Missing framework value after -f.
        exit /b 1
    )

    set "FRAMEWORK=%~2"
) else if /I "%~1"=="--framework" (
    if "%~2"=="" (
        echo ERROR: Missing framework value after --framework.
        exit /b 1
    )

    set "FRAMEWORK=%~2"
) else if /I "%ARMADA_FW_ARG1:~0,3%"=="net" (
    set "FRAMEWORK=%~1"
)
set "ARMADA_FW_ARG1="

set "FORWARD_ARGS=--framework %FRAMEWORK%"
set "DOTNET_FRAMEWORK_ARGS=--framework %FRAMEWORK% -p:TargetFramework=%FRAMEWORK% -p:TargetFrameworks=%FRAMEWORK%"
set "DOTNET_MSBUILD_FRAMEWORK_ARGS=-p:TargetFramework=%FRAMEWORK% -p:TargetFrameworks=%FRAMEWORK%"

set "ARMADA_TARGET_FRAMEWORK=%FRAMEWORK%"
set "ARMADA_FORWARD_FRAMEWORK_ARGS=%FORWARD_ARGS%"
set "ARMADA_DOTNET_FRAMEWORK_ARGS=%DOTNET_FRAMEWORK_ARGS%"
set "ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS=%DOTNET_MSBUILD_FRAMEWORK_ARGS%"

set "FRAMEWORK="
set "FORWARD_ARGS="
set "DOTNET_FRAMEWORK_ARGS="
set "DOTNET_MSBUILD_FRAMEWORK_ARGS="

REM Also honor an "ignore TLS certificate" flag (-k / --insecure) from the same command line, so every
REM script that resolves a framework transparently supports it too.
call "%~dp0resolve-insecure.bat" %*
exit /b 0
