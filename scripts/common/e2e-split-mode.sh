#!/usr/bin/env bash
# Ephemeral-image split-mode smoke test. Builds the Armada Server image fresh, boots it as a throwaway
# (--rm) container in split-mode config, waits for the unauthenticated health endpoint, asserts the
# containerized Admiral is serving, then tears everything down. This is the "ephemeral image" harness that
# validates the containerized Admiral half of the split-mode deployment without leaving any state behind.
#
# What this proves automatically:
#   - The server image builds and boots in a container with split-mode settings applied.
#   - The Admiral REST/link port (7890) and MCP port (7891) are reachable from the host, so a host-side
#     Harbor could dial in and captains could call the advertised MCP URL (127.0.0.1:7891) back.
#
# What still needs a human + a host Harbor (documented, not run here): attaching the Harbor app, minting a
# credential, and dispatching a real captain that lands a branch. The deterministic in-process equivalent of
# that path is covered by the Services.HarborSplitModeE2E Touchstone suite (real git land through the seam).
#
# Usage:
#   e2e-split-mode.sh            # build image, boot ephemeral container, health-check, tear down
#   ARMADA_E2E_KEEP=1 e2e-split-mode.sh   # leave the container running for manual Harbor attach
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

IMAGE_TAG="armada-server:e2e-split"
CONTAINER_NAME="armada-e2e-split"
HEALTH_URL="http://127.0.0.1:7890/api/v1/status/health"
MCP_URL="http://127.0.0.1:7891/mcp"

if ! command -v docker >/dev/null 2>&1; then
    echo "[e2e-split] ERROR: docker is not installed or not on PATH." >&2
    exit 1
fi

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
    -v "${REPO_ROOT}/docker/armada/armada.split.json:/app/data/armada.json:ro" \
    "${IMAGE_TAG}" >/dev/null

echo "[e2e-split] Waiting for Admiral health at ${HEALTH_URL}..."
HEALTHY=0
for _ in $(seq 1 60); do
    if ! docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
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
if curl -sf -o /dev/null "${MCP_URL}" || curl -s -o /dev/null -w '%{http_code}' "${MCP_URL}" | grep -qE '^[0-9]{3}$'; then
    echo "[e2e-split] PASS: MCP endpoint is reachable from the host."
else
    echo "[e2e-split] WARNING: MCP endpoint did not answer; a host Harbor's captains could not call home." >&2
fi

echo "[e2e-split] Split-mode ephemeral smoke test complete."
echo "[e2e-split] Next (manual): start the Harbor app on the host, point it at http://127.0.0.1:7890,"
echo "[e2e-split] mint a credential, and dispatch a captain. Re-run with ARMADA_E2E_KEEP=1 to keep the"
echo "[e2e-split] Admiral up for that step."
