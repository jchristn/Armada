#!/usr/bin/env sh
# Waits for the Armada server's health endpoint. WSL 1 counterpart of scripts/linux/healthcheck-server.sh.
# Under WSL 2 NAT networking, when nothing answers at localhost inside the distro but an Admiral on Windows does, the Windows host address is used (set WSL_NO_HOST_REWRITE=1 to disable).
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd curl "Install curl inside WSL (for example: sudo apt install curl)."
if [ "$#" -gt 0 ]; then
    BASE_URL="$1"; shift
else
    BASE_URL="$(wsl_resolve_local_url "${ARMADA_BASE_URL:-http://localhost:7890}")"
fi
wsl_run "$SCRIPT_DIR/../linux/healthcheck-server.sh" "$BASE_URL" "$@"
