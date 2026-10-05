#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLIST_LABEL="com.armada.admiral"
PLIST_DIR="${HOME}/Library/LaunchAgents"
PLIST_PATH="${PLIST_DIR}/${PLIST_LABEL}.plist"
ARMADA_HOME="${HOME}/.armada"
SERVER_EXE="${ARMADA_HOME}/bin/Armada.Server"
LOG_DIR="${ARMADA_HOME}/logs"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "ERROR: install-launchd-agent.sh must be run on macOS." >&2
    exit 1
fi

bootout_agent() {
    launchctl bootout "gui/$(id -u)" "${PLIST_PATH}" >/dev/null 2>&1 \
        || launchctl unload -w "${PLIST_PATH}" >/dev/null 2>&1 \
        || true
}

bootstrap_agent() {
    launchctl bootstrap "gui/$(id -u)" "${PLIST_PATH}" >/dev/null 2>&1 \
        || launchctl load -w "${PLIST_PATH}"
}

echo
echo "[install-launchd-agent] Publishing Armada.Server..."
"${SCRIPT_DIR}/publish-server.sh" "$@"

mkdir -p "${PLIST_DIR}" "${LOG_DIR}"

cat >"${PLIST_PATH}" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${PLIST_LABEL}</string>
    <key>ProgramArguments</key>
    <array>
        <string>${SERVER_EXE}</string>
    </array>
    <key>WorkingDirectory</key>
    <string>${ARMADA_HOME}</string>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>${LOG_DIR}/launchd-stdout.log</string>
    <key>StandardErrorPath</key>
    <string>${LOG_DIR}/launchd-stderr.log</string>
</dict>
</plist>
EOF

echo
echo "[install-launchd-agent] Loading ${PLIST_LABEL}..."
bootout_agent
bootstrap_agent

"${SCRIPT_DIR}/healthcheck-server.sh"

echo
echo "[install-launchd-agent] Health check complete."

echo
echo "[install-launchd-agent] Completed."
echo "[install-launchd-agent] Agent file: ${PLIST_PATH}"
