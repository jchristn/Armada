#!/bin/bash

if [ -z "$1" ]; then
    echo "Usage: ./build-server.sh <version-tag>"
    echo "Example: ./build-server.sh v0.2.0"
    exit 1
fi

TAG="$1"

docker buildx build \
    --platform linux/amd64,linux/arm64/v8 \
    -f src/Armada.Server/Dockerfile \
    -t jchristn77/armada-server:latest \
    -t "jchristn77/armada-server:${TAG}" \
    --push \
    .
