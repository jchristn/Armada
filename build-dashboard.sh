#!/bin/bash

TAG="$1"

if [ -z "$TAG" ]; then
    echo "Building jchristn77/armada-dashboard:latest"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Dashboard/Dockerfile \
        -t jchristn77/armada-dashboard:latest \
        --push \
        .
else
    echo "Building jchristn77/armada-dashboard:latest and jchristn77/armada-dashboard:${TAG}"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Dashboard/Dockerfile \
        -t jchristn77/armada-dashboard:latest \
        -t "jchristn77/armada-dashboard:${TAG}" \
        --push \
        .
fi
