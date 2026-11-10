#!/usr/bin/env bash
# Build the jchristn77/armada-server Docker image for linux/amd64 and linux/arm64/v8 with docker buildx, push it
# to Docker Hub as :latest (and :<tag> when given), then pull the pushed tags back into the local image store.
# Needs Docker with buildx, logged in to Docker Hub with push access.
# WSL 2 version: uses the docker CLI inside this distro.
# That is Docker Desktop with WSL integration for this distro, or Docker installed in the distro.
#
# Usage: scripts/wsl2/build-server.sh [<tag>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
# The helper's own errors get the same "ERROR:" prefix as every other Armada script.
wsl_die() { die "$@"; }
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
case "${1:-}" in -*) echo "ERROR: Unexpected argument: $1 (run with -h for usage)" >&2; exit 2 ;; esac
[ "$#" -le 1 ] || { echo "ERROR: Unexpected argument: $2 (run with -h for usage)" >&2; exit 2; }
TAG="${1:-}"
IMAGE="jchristn77/armada-server"

# Docker Desktop's credential helper (docker-credential-desktop.exe) lives in a Windows folder that
# wsl_sanitize_path dropped from PATH; put that one folder back (at the end), so docker login, push and pull keep
# working. Without it, a config that names the helper fails clearly here rather than halfway through a push.
docker_credential_dir() {
    local dir old_ifs="$IFS"
    IFS=:
    for dir in $WSL_ORIGINAL_PATH; do
        if [ -f "$dir/docker-credential-desktop.exe" ]; then
            IFS="$old_ifs"
            printf '%s\n' "$dir"
            return 0
        fi
    done
    IFS="$old_ifs"
    return 1
}
if CREDENTIAL_DIR="$(docker_credential_dir)"; then
    case ":$PATH:" in *":$CREDENTIAL_DIR:"*) ;; *) PATH="$PATH:$CREDENTIAL_DIR"; export PATH ;; esac
elif grep -Eq '"credsStore"[[:space:]]*:[[:space:]]*"desktop(\.exe)?"' "${DOCKER_CONFIG:-$HOME/.docker}/config.json" 2>/dev/null; then
    die "Docker is set to use Docker Desktop's credential helper (credsStore in ${DOCKER_CONFIG:-$HOME/.docker}/config.json), but docker-credential-desktop.exe is not on the Windows PATH. Turn on Docker Desktop's WSL integration for this distro, or remove the credsStore line."
fi
wsl_require_cmd docker "Install Docker inside WSL, or turn on Docker Desktop's WSL integration for this distro (Settings > Resources > WSL integration)."
docker info >/dev/null 2>&1 || die "docker is installed but its engine is not reachable. Start Docker Desktop (with WSL integration for this distro) or the docker service in this distro (sudo service docker start)."
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
