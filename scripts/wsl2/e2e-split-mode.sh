#!/usr/bin/env bash
# Ephemeral-image split-mode smoke test. Builds the Armada Server image fresh, boots it as a throwaway (--rm)
# container in split-mode config, waits for the unauthenticated health endpoint, checks that the Admiral REST/link
# port (7890) and MCP port (7891) answer from the host, then tears everything down. Attaching a host Harbor and
# dispatching a real captain stay manual. The container listens on 0.0.0.0, so the script passes
# ARMADA_INITIAL_ADMIN_PASSWORD (yours when set, otherwise a generated throwaway value).
# Set ARMADA_E2E_KEEP=1 to leave the container running for a manual Harbor attach.
# WSL 2 version: uses the docker CLI in this distro (Docker Desktop with WSL integration, or Docker installed in
# the distro). All WSL 2 distros share one network, so 127.0.0.1 reaches the published ports.
#
# Usage: scripts/wsl2/e2e-split-mode.sh
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
if [ "$#" -gt 0 ]; then
    echo "ERROR: Unexpected argument: $1 (this script takes no arguments; run with -h for usage)" >&2
    exit 2
fi

IMAGE_TAG="armada-server:e2e-split"
CONTAINER_NAME="armada-e2e-split"
HEALTH_URL="http://127.0.0.1:7890/api/v1/status/health"
MCP_URL="http://127.0.0.1:7891/mcp"
ADMIN_PASSWORD="${ARMADA_INITIAL_ADMIN_PASSWORD:-E2e-Split-${RANDOM}${RANDOM}-Pw1}"

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
wsl_require_cmd curl "Install curl inside WSL (for example: sudo apt install curl)."

cleanup() {
    if [ "${ARMADA_E2E_KEEP:-0}" = "1" ]; then
        echo "[e2e-split] ARMADA_E2E_KEEP=1 -- leaving container '${CONTAINER_NAME}' running for manual Harbor attach."
        return
    fi
    echo "[e2e-split] Tearing down ephemeral container..."
    docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

echo "[e2e-split] Building server image (${IMAGE_TAG})..."
docker build -t "${IMAGE_TAG}" -f "${REPO_ROOT}/src/Armada.Server/Dockerfile" "${REPO_ROOT}"

# Remove any leftover container from a prior aborted run.
docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true

echo "[e2e-split] Booting ephemeral Admiral (split mode)..."
docker run -d --rm \
    --name "${CONTAINER_NAME}" \
    -p 7890:7890 -p 7891:7891 -p 9464:9464 \
    -e "ARMADA_INITIAL_ADMIN_PASSWORD=${ADMIN_PASSWORD}" \
    -v "${REPO_ROOT}/docker/armada/armada.split.json:/app/data/settings.json:ro" \
    "${IMAGE_TAG}" >/dev/null

echo "[e2e-split] Waiting for Admiral health at ${HEALTH_URL}..."
HEALTHY=0
for _ in $(seq 1 60); do
    # Capture first: under pipefail, grep -q exiting early can fail the docker ps side of a pipe.
    RUNNING_NAMES="$(docker ps --format '{{.Names}}' 2>/dev/null || true)"
    if ! printf '%s\n' "$RUNNING_NAMES" | awk -v n="${CONTAINER_NAME}" '$0 == n { found = 1 } END { exit !found }'; then
        echo "[e2e-split] ERROR: container exited before becoming healthy. Logs:" >&2
        docker logs "${CONTAINER_NAME}" 2>&1 | tail -40 >&2 || true
        exit 1
    fi
    if curl -sf -o /dev/null "${HEALTH_URL}"; then
        HEALTHY=1
        break
    fi
    sleep 1
done

if [ "${HEALTHY}" -ne 1 ]; then
    echo "[e2e-split] ERROR: Admiral did not become healthy within 60s. Logs:" >&2
    docker logs "${CONTAINER_NAME}" 2>&1 | tail -40 >&2 || true
    exit 1
fi
echo "[e2e-split] PASS: containerized Admiral is healthy."

# The MCP port must accept connections so a host Harbor's captains can call home. An unauthenticated
# probe is expected to be rejected at the JSON-RPC layer (not connection-refused); either a response or a
# well-formed error means the port is served.
echo "[e2e-split] Probing MCP endpoint at ${MCP_URL}..."
MCP_STATUS="$(curl -s -o /dev/null -w '%{http_code}' "${MCP_URL}" 2>/dev/null || true)"
if [ -n "$MCP_STATUS" ] && [ "$MCP_STATUS" != "000" ]; then
    echo "[e2e-split] PASS: MCP endpoint is reachable from the host."
else
    echo "[e2e-split] WARNING: MCP endpoint did not answer; a host Harbor's captains could not call home." >&2
fi

echo "[e2e-split] Split-mode ephemeral smoke test complete."
echo "[e2e-split] Next (manual): mint a credential, start the Harbor app on the host with its link URL set to"
echo "[e2e-split] ws://127.0.0.1:7890/v1.0/harbor/connect and that credential as its AccessKey, and dispatch a"
echo "[e2e-split] captain. Re-run with ARMADA_E2E_KEEP=1 to keep the Admiral up for that step."
