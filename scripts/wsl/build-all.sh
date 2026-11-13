#!/usr/bin/env bash
# Build, push and pull all three Armada Docker images in turn (armada-server, armada-dashboard, armada-proxy),
# each with the same optional tag, the same way build-server.sh, build-dashboard.sh and build-proxy.sh do.
# Stops at the first failure.
# WSL 1 version: uses the docker CLI inside this distro (see build-server.sh for the Docker requirements).
#
# Usage: scripts/wsl/build-all.sh [<tag>]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
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
# WSL 1 cannot run a Docker engine itself. A docker CLI that reaches one anyway works: Docker Desktop's daemon
# exposed on tcp://localhost:2375 (DOCKER_HOST), or a docker context pointing at a remote engine.
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    die "WSL 1 cannot run a Docker engine. Run the scripts\\windows version from Windows with Docker Desktop, move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2, or point the docker CLI at a reachable engine (Docker Desktop's daemon exposed on tcp://localhost:2375 with DOCKER_HOST set, or a docker context)."
fi
docker buildx version >/dev/null 2>&1 || die "docker buildx is not available. Install the Docker buildx plugin: https://docs.docker.com/build/install-buildx/"

cd "$REPO_ROOT"

# Usage: build_image <label> <image> <dockerfile>
build_image() {
    local tags=(-t "$2:latest")
    echo "=== Building Armada $1 ==="
    if [ -z "$TAG" ]; then
        echo "Building $2:latest"
    else
        echo "Building $2:latest and $2:${TAG}"
        tags+=(-t "$2:${TAG}")
    fi
    docker buildx build --platform linux/amd64,linux/arm64/v8 -f "$3" "${tags[@]}" --push .
    # Pull the pushed image back into the local registry (from Docker Hub, not the builder).
    echo "Pulling $2:latest into local registry"
    docker pull "$2:latest"
    if [ -n "$TAG" ]; then
        echo "Pulling $2:${TAG} into local registry"
        docker pull "$2:${TAG}"
    fi
}

build_image server jchristn77/armada-server src/Armada.Server/Dockerfile
build_image dashboard jchristn77/armada-dashboard src/Armada.Dashboard/Dockerfile
build_image proxy jchristn77/armada-proxy src/Armada.Proxy/Dockerfile

echo "=== All Armada images built, pushed to Docker Hub, and pulled into the local registry ==="
