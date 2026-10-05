#!/usr/bin/env bash
# =============================================================================
#  update.sh - pull the Armada images named in a compose file and recreate the stack.
#
#  Non-destructive: named volumes and the bind-mounted db/ and logs/ folders are
#  preserved. For a destructive reset use docker/armada/factory/reset.sh.
#
#  Usage:   ./update.sh [compose-file]
#  Default: docker/armada/compose.yaml
#  Example: ./update.sh armada/compose.split.yaml
#           ./update.sh proxy/compose.yaml
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${1:-armada/compose.yaml}"
case "$COMPOSE_FILE" in
  /*) ;;
  *) COMPOSE_FILE="${SCRIPT_DIR}/${COMPOSE_FILE}" ;;
esac

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "Compose file not found: $COMPOSE_FILE"
  exit 1
fi

echo "========================================"
echo " Armada Update"
echo " Compose file: $COMPOSE_FILE"
echo "========================================"

echo
echo "[1/4] Pulling the images named in the compose file..."
# The compose files pin release tags (for example jchristn77/armada-server:v1.0.0); edit the tags to move
# to another release before running this.
docker compose -f "$COMPOSE_FILE" pull

echo
echo "[2/4] Stopping the stack (volumes are preserved)..."
docker compose -f "$COMPOSE_FILE" down

echo
echo "[3/4] Starting the stack..."
docker compose -f "$COMPOSE_FILE" up -d

echo
echo "[4/4] Container status:"
docker ps -a

echo
echo "Update complete."
