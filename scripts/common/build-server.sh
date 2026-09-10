#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
TAG="$1"

cd "$REPO_ROOT"

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

# Pull the pushed image back into the local registry (from Docker Hub, not the
# builder) so the same tags are available locally as well.
echo "Pulling jchristn77/armada-server:latest into local registry"
docker pull jchristn77/armada-server:latest
if [ -n "$TAG" ]; then
    echo "Pulling jchristn77/armada-server:${TAG} into local registry"
    docker pull "jchristn77/armada-server:${TAG}"
fi
