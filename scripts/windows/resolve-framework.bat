@echo off
set "FRAMEWORK=%ARMADA_TARGET_FRAMEWORK%"
if "%FRAMEWORK%"=="" set "FRAMEWORK=net10.0"

REM Only a leading "net*" token counts as a bare framework (matching resolve-framework.sh), so a first
REM argument such as --insecure is not mistaken for the framework.
set "ARMADA_FW_ARG1=%~1"
if not defined ARMADA_FW_ARG1 set "ARMADA_FW_ARG1=-"

if /I "%~1"=="-f" (
    if "%~2"=="" (
        echo ERROR: Missing framework value after -f. 1>&2
        exit /b 1
    )

    set "FRAMEWORK=%~2"
) else if /I "%~1"=="--framework" (
    if "%~2"=="" (
        echo ERROR: Missing framework value after --framework. 1>&2
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

REM Record the first argument that is neither the framework selection nor an insecure flag in ARMADA_UNEXPECTED_ARG.
REM Callers that take nothing else reject it ("if defined ARMADA_UNEXPECTED_ARG"); callers that forward the rest
REM (run-tui, run-harbor) ignore it. shift does not change the %* used above.
set "ARMADA_UNEXPECTED_ARG="
set "ARMADA_FW_SKIP=0"
if /I "%~1"=="-f" set "ARMADA_FW_SKIP=2"
if /I "%~1"=="--framework" set "ARMADA_FW_SKIP=2"
set "ARMADA_FW_ARG1=%~1"
if not defined ARMADA_FW_ARG1 set "ARMADA_FW_ARG1=-"
if "%ARMADA_FW_SKIP%"=="0" if /I "%ARMADA_FW_ARG1:~0,3%"=="net" set "ARMADA_FW_SKIP=1"
set "ARMADA_FW_ARG1="
:armada_scan_args
if "%~1"=="" goto :armada_scan_done
if not "%ARMADA_FW_SKIP%"=="0" goto :armada_scan_skip
if /I "%~1"=="-k" goto :armada_scan_next
if /I "%~1"=="--insecure" goto :armada_scan_next
if /I "%~1"=="--no-strict-ssl" goto :armada_scan_next
if /I "%~1"=="--ignore-cert-errors" goto :armada_scan_next
set "ARMADA_UNEXPECTED_ARG=%~1"
goto :armada_scan_done
:armada_scan_skip
set /a ARMADA_FW_SKIP-=1
:armada_scan_next
shift
goto :armada_scan_args
:armada_scan_done
set "ARMADA_FW_SKIP="
exit /b 0
