#!/usr/bin/env bash
# =============================================================================
#  reset.sh - reseed the demo Admiral and restart it (docs/DEMO_ADMIRAL.md).
#
#  Destructive for ./data: the seeded database is deleted and seeded again, so
#  every change a reviewer made is undone. Run it nightly from cron to keep the
#  demo as seeded, for example:
#    15 3 * * * $HOME/armada/docker/demo/reset.sh >> $HOME/demo-reset.log 2>&1
#
#  Needs Docker. The seeder runs from this checkout in the .NET SDK image.
#  Passwords come from the environment or ./.env next to this file (never Git):
#    ARMADA_DEMO_ADMIN_PASSWORD, ARMADA_DEMO_REVIEWER_PASSWORD (8+ characters)
#    ARMADA_DEMO_TUNNEL_URL and ARMADA_DEMO_TUNNEL_PASSWORD (optional, remote control)
#
#  To pull a newer server image without reseeding, use ../update.sh demo/compose.yaml.
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
COMPOSE_FILE="${SCRIPT_DIR}/compose.yaml"
DATA_DIR="${SCRIPT_DIR}/data"

if [ -f "${SCRIPT_DIR}/.env" ]; then
  set -a; . "${SCRIPT_DIR}/.env"; set +a
fi
: "${ARMADA_DEMO_ADMIN_PASSWORD:?Set ARMADA_DEMO_ADMIN_PASSWORD (8+ characters)}"
: "${ARMADA_DEMO_REVIEWER_PASSWORD:?Set ARMADA_DEMO_REVIEWER_PASSWORD (8+ characters)}"

SEED_ARGS="--data-dir /data --settings-data-dir /app/data --hostname 0.0.0.0"
if [ -n "${ARMADA_DEMO_TUNNEL_URL:-}" ]; then
  SEED_ARGS="${SEED_ARGS} --tunnel-url ${ARMADA_DEMO_TUNNEL_URL}"
fi

echo "[1/3] Stopping the demo Admiral..."
docker compose -f "$COMPOSE_FILE" down

echo "[2/3] Reseeding ${DATA_DIR}..."
rm -rf "${DATA_DIR}"
mkdir -p "${DATA_DIR}/logs"
docker run --rm \
  -e ARMADA_DEMO_ADMIN_PASSWORD -e ARMADA_DEMO_REVIEWER_PASSWORD -e ARMADA_DEMO_TUNNEL_PASSWORD \
  -v "${REPO_ROOT}:/src:ro" -v "${DATA_DIR}:/data" \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "cp -r /src /tmp/armada && dotnet run --project /tmp/armada/src/Armada.DemoSeed -f net10.0 -- ${SEED_ARGS}"
# The server image runs as UID 1654.
docker run --rm -v "${DATA_DIR}:/data" alpine chown -R 1654:1654 /data

echo "[3/3] Starting the demo Admiral..."
docker compose -f "$COMPOSE_FILE" up -d
docker compose -f "$COMPOSE_FILE" ps
echo "Ids for the screenshot script: ${DATA_DIR}/demo-ids.json"
