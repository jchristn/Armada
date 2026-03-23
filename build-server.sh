#!/bin/bash

TAG="$1"

if [ -z "$TAG" ]; then
    echo "Building jchristn77/armada-server:latest"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Server/Dockerfile \
        -t jchristn77/armada-server:latest \
        --push \
        .
else
    echo "Building jchristn77/armada-server:latest and jchristn77/armada-server:${TAG}"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Server/Dockerfile \
        -t jchristn77/armada-server:latest \
        -t "jchristn77/armada-server:${TAG}" \
        --push \
        .
fi
