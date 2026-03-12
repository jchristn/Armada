@echo off

if "%~1"=="" (
    echo Usage: build-server.bat ^<version-tag^>
    echo Example: build-server.bat v0.2.0
    exit /b 1
)

docker buildx build ^
    --platform linux/amd64,linux/arm64/v8 ^
    -f src/Armada.Server/Dockerfile ^
    -t jchristn77/armada-server:latest ^
    -t jchristn77/armada-server:%~1 ^
    --push ^
    .
