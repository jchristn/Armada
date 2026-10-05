#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLIST_LABEL="com.armada.admiral"
PLIST_PATH="${HOME}/Library/LaunchAgents/${PLIST_LABEL}.plist"

if [ ! -f "${PLIST_PATH}" ]; then
    echo "ERROR: ${PLIST_PATH} was not found. Run install-launchd-agent.sh first." >&2
    exit 1
fi

if [ "$(uname -s)" != "Darwin" ]; then
    echo "ERROR: update-launchd-agent.sh must be run on macOS." >&2
    exit 1
fi

echo
echo "[update-launchd-agent] Unloading ${PLIST_LABEL}..."
launchctl bootout "gui/$(id -u)" "${PLIST_PATH}" >/dev/null 2>&1 \
    || launchctl unload -w "${PLIST_PATH}" >/dev/null 2>&1 \
    || true

"${SCRIPT_DIR}/install-launchd-agent.sh" "$@"
