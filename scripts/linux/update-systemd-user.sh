#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_NAME="armada.service"
UNIT_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/systemd/user"
UNIT_PATH="${UNIT_DIR}/${UNIT_NAME}"

if [ ! -f "${UNIT_PATH}" ]; then
    echo "ERROR: ${UNIT_PATH} was not found. Run install-systemd-user.sh first." >&2
    exit 1
fi

if command -v systemctl >/dev/null 2>&1; then
    echo
    echo "[update-systemd-user] Stopping ${UNIT_NAME}..."
    systemctl --user stop "${UNIT_NAME}" >/dev/null 2>&1 || true
fi

"${SCRIPT_DIR}/install-systemd-user.sh" "$@"
