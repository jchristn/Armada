#!/usr/bin/env bash
# Build the jchristn77/armada-server Docker image for linux/amd64 and linux/arm64/v8 with docker buildx, push it
# to Docker Hub as :latest (and :<tag> when given), then pull the pushed tags back into the local image store.
# Needs Docker with buildx, logged in to Docker Hub with push access.
#
# Usage: build-server.sh [<tag>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; -*) echo "ERROR: Unexpected argument: $1 (run with -h for usage)" >&2; exit 2 ;; esac
[ "$#" -le 1 ] || { echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2; exit 2; }
TAG="${1:-}"
IMAGE="jchristn77/armada-server"

command -v docker >/dev/null 2>&1 || die "docker was not found on PATH. Install Docker: https://docs.docker.com/get-docker/"
docker info >/dev/null 2>&1 || die "The Docker engine is not reachable. Start Docker (Docker Desktop or the docker service) and try again."
docker buildx version >/dev/null 2>&1 || die "docker buildx is not available. Install the Docker buildx plugin: https://docs.docker.com/build/install-buildx/"

cd "$REPO_ROOT"

TAGS=(-t "${IMAGE}:latest")
if [ -z "$TAG" ]; then
    echo "Building ${IMAGE}:latest"
else
    echo "Building ${IMAGE}:latest and ${IMAGE}:${TAG}"
    TAGS+=(-t "${IMAGE}:${TAG}")
fi
docker buildx build \
    --platform linux/amd64,linux/arm64/v8 \
    -f src/Armada.Server/Dockerfile \
    "${TAGS[@]}" \
    --push \
    .

# Pull the pushed image back into the local registry (from Docker Hub, not the
# builder) so the same tags are available locally as well.
echo "Pulling ${IMAGE}:latest into local registry"
docker pull "${IMAGE}:latest"
if [ -n "$TAG" ]; then
    echo "Pulling ${IMAGE}:${TAG} into local registry"
    docker pull "${IMAGE}:${TAG}"
fi
