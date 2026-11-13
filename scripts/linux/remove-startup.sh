#!/usr/bin/env bash
# Unregister Armada from starting at login, then stop the running server: disables and deletes the systemd user
# unit armada.service and deletes the XDG autostart entry armada.desktop that install-startup.sh writes. Succeeds
# with "nothing to remove" when neither exists. Fails when Armada is registered by "armada-server --install-service"
# (that registration is removed with --uninstall-service). The published files in ~/.armada stay.
#
# Usage: scripts/linux/remove-startup.sh [<framework> | -f <framework> | --framework <framework>]
#   The framework is accepted for parity with the other scripts and is not used.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMMON_DIR="$(cd "${SCRIPT_DIR}/../common" && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

[ "$(uname -s)" = Linux ] || die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
if grep -qi microsoft /proc/version 2>/dev/null \
    && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=../common/resolve-framework.sh
. "${COMMON_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"

UNIT_NAME="armada.service"
UNIT_PATH="${XDG_CONFIG_HOME:-${HOME}/.config}/systemd/user/${UNIT_NAME}"
DESKTOP_PATH="${XDG_CONFIG_HOME:-${HOME}/.config}/autostart/armada.desktop"
ARMADA_HOME="${HOME}/.armada"
SERVER_EXE="${ARMADA_HOME}/bin/Armada.Server"
export ARMADA_SCRIPT_OS=linux

# "armada-server --install-service" registers the Admiral itself: the same user unit path running
# "Armada.Server --run-service", or /etc/systemd/system/armada.service as root. Two registrations would start two
# Admirals on the same ports, so the startup scripts refuse to work next to one (the same rule on every OS).
refuse_product_registration() {
    if [ -f "$UNIT_PATH" ] && grep -q -- '--run-service' "$UNIT_PATH"; then
        die "Armada is registered by 'armada-server --install-service' (${UNIT_PATH}), which runs its own Admiral. Remove that first with: ${SERVER_EXE} --uninstall-service (or armada-server --uninstall-service), then run this again."
    fi
    if [ -f /etc/systemd/system/armada.service ]; then
        die "Armada is registered as a system service by 'armada-server --install-service' (/etc/systemd/system/armada.service). Remove that first with: sudo armada-server --uninstall-service (or sudo ${SERVER_EXE} --uninstall-service), then run this again."
    fi
}

# Captains are started by their bare command names (claude, codex, git, ...), and a systemd user manager or a
# desktop session does not have this shell's PATH, so the PATH at install time is written into the unit and entry.
# systemd: inside a quoted value, \ and " are escaped and % is a specifier, written %%.
systemd_quote() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/%/%%/g'; }
# Desktop entry Exec: the string escape doubles \ before the quoting rule, so a literal \ is four backslashes;
# ", `, $ are backslash-escaped inside quotes and % is a field code, written %%.
desktop_quote() { printf '%s' "$1" | sed -e 's/\\/\\\\\\\\/g' -e 's/["`$]/\\&/g' -e 's/%/%%/g'; }

write_unit() {
    mkdir -p "$(dirname "$UNIT_PATH")"
    cat >"${UNIT_PATH}" <<UNIT
[Unit]
Description=Armada Admiral Server

[Service]
Type=simple
ExecStart=${SERVER_EXE}
WorkingDirectory=${ARMADA_HOME}
Environment="PATH=$(systemd_quote "$PATH")"
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT
}

write_desktop() {
    mkdir -p "$(dirname "$DESKTOP_PATH")" "${ARMADA_HOME}/logs"
    cat >"${DESKTOP_PATH}" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Armada Admiral
Comment=Armada Admiral server
Exec=env "PATH=$(desktop_quote "$PATH")" "$(desktop_quote "$SERVER_EXE")"
Path=${ARMADA_HOME}
Terminal=false
NoDisplay=true
X-GNOME-Autostart-enabled=true
DESKTOP
}

refuse_product_registration
if [ ! -f "$UNIT_PATH" ] && [ ! -f "$DESKTOP_PATH" ]; then
    echo "[remove-startup] Armada is not registered to start at login; nothing to remove."
    exit 0
fi

echo
echo "[remove-startup] Unregistering Armada from starting at login..."
if [ -f "$UNIT_PATH" ]; then
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --user disable --now "${UNIT_NAME}" >/dev/null 2>&1 || true
    fi
    rm -f "${UNIT_PATH}"
    if command -v systemctl >/dev/null 2>&1; then
        systemctl --user daemon-reload >/dev/null 2>&1 || true
        systemctl --user reset-failed "${UNIT_NAME}" >/dev/null 2>&1 || true
    fi
    echo "[remove-startup] Removed ${UNIT_PATH}"
fi
if [ -f "$DESKTOP_PATH" ]; then
    rm -f "$DESKTOP_PATH"
    echo "[remove-startup] Removed ${DESKTOP_PATH}"
fi

echo
echo "[remove-startup] Stopping Armada.Server..."
"${COMMON_DIR}/stop-armada-server.sh" || die "Armada.Server is still running and could not be stopped."

echo
echo "[remove-startup] Completed."
