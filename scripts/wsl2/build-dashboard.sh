#!/usr/bin/env sh
# Builds and pushes the Armada Docker image(s) with docker buildx. WSL 2 counterpart of scripts/linux/build-dashboard.sh.
# Needs a working Docker engine (Docker Desktop WSL integration or Docker installed in the distro).
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd docker "Install Docker inside WSL or turn on Docker Desktop's WSL integration for this distro."
docker info >/dev/null 2>&1 || wsl_die "Docker is installed but its engine is not reachable. Start Docker Desktop (with WSL integration for this distro) or the docker service."
wsl_run "$SCRIPT_DIR/../linux/build-dashboard.sh" "$@"
