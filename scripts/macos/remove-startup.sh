#!/usr/bin/env bash
# Unregister Armada from starting at login, then stop the running server: unloads the launchd agent
# com.armada.admiral, deletes ~/Library/LaunchAgents/com.armada.admiral.plist and stops any Armada server left.
# Succeeds with "nothing to remove" when the agent is not registered. Fails when Armada is registered by
# "armada-server --install-service" or the .pkg installer (that is removed with --uninstall-service). The published
# files in ~/.armada stay.
#
# Usage: scripts/macos/remove-startup.sh [<framework> | -f <framework> | --framework <framework>]
#   The framework is accepted for parity with the other scripts and is not used.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMMON_DIR="$(cd "${SCRIPT_DIR}/../common" && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

[ "$(uname -s)" = Darwin ] || die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 or scripts/wsl on WSL, or scripts\\windows on Windows."
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=../common/resolve-framework.sh
. "${COMMON_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

PLIST_LABEL="com.armada.admiral"
PLIST_DIR="${HOME}/Library/LaunchAgents"
PLIST_PATH="${PLIST_DIR}/${PLIST_LABEL}.plist"
ARMADA_HOME="${HOME}/.armada"
SERVER_EXE="${ARMADA_HOME}/bin/Armada.Server"
LOG_DIR="${ARMADA_HOME}/logs"
export ARMADA_SCRIPT_OS=macos

# "armada-server --install-service" (and the .pkg installer) registers the Admiral itself under the label
# com.joelchristner.armada.server, in ~/Library/LaunchAgents or, as root, /Library/LaunchAgents. Two registrations
# would start two Admirals on the same ports, so the startup scripts refuse to work next to one (the same rule on
# every OS).
refuse_product_registration() {
    local plist
    for plist in "${HOME}/Library/LaunchAgents/com.joelchristner.armada.server.plist" /Library/LaunchAgents/com.joelchristner.armada.server.plist; do
        if [ -f "$plist" ]; then
            die "Armada is registered by 'armada-server --install-service' or the .pkg installer (${plist}), which runs its own Admiral. Remove that first with: ${SERVER_EXE} --uninstall-service (or armada-server --uninstall-service; with sudo for /Library), then run this again."
        fi
    done
}

# launchd starts agents with PATH=/usr/bin:/bin:/usr/sbin:/sbin, and captains are started by their bare command
# names (claude, codex, git, ...), so this shell's PATH is written into the plist (XML-escaped).
xml_escape() { printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g'; }

write_plist() {
    mkdir -p "${PLIST_DIR}" "${LOG_DIR}"
    cat >"${PLIST_PATH}" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>${PLIST_LABEL}</string>
    <key>ProgramArguments</key>
    <array>
        <string>$(xml_escape "$SERVER_EXE")</string>
    </array>
    <key>WorkingDirectory</key>
    <string>$(xml_escape "$ARMADA_HOME")</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PATH</key>
        <string>$(xml_escape "$PATH")</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>$(xml_escape "$LOG_DIR")/launchd-stdout.log</string>
    <key>StandardErrorPath</key>
    <string>$(xml_escape "$LOG_DIR")/launchd-stderr.log</string>
</dict>
</plist>
PLIST
    plutil -lint "${PLIST_PATH}" >/dev/null || die "The generated ${PLIST_PATH} is not a valid property list."
}

refuse_product_registration
if [ ! -f "${PLIST_PATH}" ]; then
    echo "[remove-startup] Armada is not registered to start at login; nothing to remove."
    exit 0
fi

echo
echo "[remove-startup] Unregistering Armada from starting at login..."
launchctl bootout "gui/$(id -u)" "${PLIST_PATH}" >/dev/null 2>&1 || true
rm -f "${PLIST_PATH}"
echo "[remove-startup] Removed ${PLIST_PATH}"

echo
echo "[remove-startup] Stopping Armada.Server..."
"${COMMON_DIR}/stop-armada-server.sh" || die "Armada.Server is still running and could not be stopped."

echo
echo "[remove-startup] Completed."
