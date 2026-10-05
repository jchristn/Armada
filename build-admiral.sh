#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-admiral.sh <tag>"
    echo "Example: build-admiral.sh v1.0.0"
    exit 1
fi

TAG="$1"
IMAGE="jchristn77/armada-server"

cd "$(dirname "${BASH_SOURCE[0]}")"

# Build on the cloud builder for both architectures and push the multi-arch manifest to Docker Hub.
echo "Building ${IMAGE}:latest and ${IMAGE}:${TAG}..."
docker buildx build \
    --builder cloud-jchristn77-jchristn77 \
    --platform linux/amd64,linux/arm64/v8 \
    -t "${IMAGE}:latest" \
    -t "${IMAGE}:${TAG}" \
    -f src/Armada.Server/Dockerfile \
    --push \
    .

# Pull the pushed tags back from Docker Hub to update the local copy.
echo "Pulling ${IMAGE}:latest..."
docker pull "${IMAGE}:latest"

echo "Pulling ${IMAGE}:${TAG}..."
docker pull "${IMAGE}:${TAG}"

echo "Done."
