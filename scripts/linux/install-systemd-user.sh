#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNIT_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/systemd/user"
UNIT_NAME="armada.service"
UNIT_PATH="${UNIT_DIR}/${UNIT_NAME}"
ARMADA_HOME="${HOME}/.armada"
SERVER_EXE="${ARMADA_HOME}/bin/Armada.Server"

if ! command -v systemctl >/dev/null 2>&1; then
    echo "ERROR: systemctl is required to install the systemd user service." >&2
    exit 1
fi

echo
echo "[install-systemd-user] Publishing Armada.Server..."
"${SCRIPT_DIR}/publish-server.sh" "$@"

mkdir -p "${UNIT_DIR}"

cat >"${UNIT_PATH}" <<EOF
[Unit]
Description=Armada Admiral Server

[Service]
Type=simple
ExecStart=${SERVER_EXE}
WorkingDirectory=${ARMADA_HOME}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
EOF

echo
echo "[install-systemd-user] Reloading systemd user units..."
systemctl --user daemon-reload
systemctl --user enable --now "${UNIT_NAME}" >/dev/null

"${SCRIPT_DIR}/healthcheck-server.sh"

echo
echo "[install-systemd-user] Completed."
echo "[install-systemd-user] Unit file: ${UNIT_PATH}"
