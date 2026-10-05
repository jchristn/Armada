#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
TAG="$1"

cd "$REPO_ROOT"

if [ -z "$TAG" ]; then
    echo "Building jchristn77/armada-dashboard:latest"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Dashboard/Dockerfile \
        -t jchristn77/armada-dashboard:latest \
        --push \
        . || exit 1
else
    echo "Building jchristn77/armada-dashboard:latest and jchristn77/armada-dashboard:${TAG}"
    docker buildx build \
        --platform linux/amd64,linux/arm64/v8 \
        -f src/Armada.Dashboard/Dockerfile \
        -t jchristn77/armada-dashboard:latest \
        -t "jchristn77/armada-dashboard:${TAG}" \
        --push \
        . || exit 1
fi

# Pull the pushed image back into the local registry (from Docker Hub, not the
# builder) so the same tags are available locally as well.
echo "Pulling jchristn77/armada-dashboard:latest into local registry"
docker pull jchristn77/armada-dashboard:latest || exit 1
if [ -n "$TAG" ]; then
    echo "Pulling jchristn77/armada-dashboard:${TAG} into local registry"
    docker pull "jchristn77/armada-dashboard:${TAG}" || exit 1
fi
