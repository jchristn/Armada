@echo off
setlocal

echo.
echo [install] Deploying dashboard...
call "%~dp0deploy-dashboard.bat"
if errorlevel 1 exit /b 1

echo.
echo [install] Building Armada solution...
dotnet build src/Armada.sln
if errorlevel 1 exit /b 1

echo.
echo [install] Packing Armada.Helm...
dotnet pack src/Armada.Helm -o ./src/nupkg
if errorlevel 1 exit /b 1

echo.
echo [install] Installing Armada.Helm as a global tool...
dotnet tool install --global --add-source ./src/nupkg Armada.Helm

echo.
echo [install] Completed.
exit /b %ERRORLEVEL%
