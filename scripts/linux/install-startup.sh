#!/usr/bin/env bash
# Publish Armada.Server into ~/.armada/bin, register it to start at login and start it now, then wait for its
# health check. Uses the systemd user unit ~/.config/systemd/user/armada.service (Restart=on-failure) when a
# systemd user manager is running; otherwise an XDG autostart entry ~/.config/autostart/armada.desktop, which a
# desktop session runs at graphical login. Either one carries this shell's PATH, so the server finds the captain
# CLIs (claude, codex, git, ...) at login. Stops any running Armada server first. Fails when Armada is already
# registered (use update-startup.sh) or registered by "armada-server --install-service".
#
# Usage: scripts/linux/install-startup.sh [<framework> | -f <framework> | --framework <framework>] [-k|--insecure]
#   <framework>  net10.0 (default, or ARMADA_TARGET_FRAMEWORK) or net8.0
#   --insecure   also -k, --no-strict-ssl, --ignore-cert-errors: skip TLS certificate checks for npm/Node only
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

command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"
command -v curl >/dev/null 2>&1 || die "curl was not found on PATH; the health check needs it. Install curl with your package manager."

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
if [ -f "$UNIT_PATH" ] || [ -f "$DESKTOP_PATH" ]; then
    die "Armada is already registered to start at login. Run scripts/linux/update-startup.sh to republish and restart it."
fi

# A systemd user manager is reachable (systemctl alone is not enough: containers and some init systems have none).
if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    MODE=systemd
else
    MODE=autostart
fi

echo
echo "[install-startup] Stopping any running Armada server..."
"${COMMON_DIR}/stop-armada-server.sh" || die "Armada.Server is still running and could not be stopped."

echo
echo "[install-startup] Publishing Armada.Server..."
"${COMMON_DIR}/publish-server.sh" "$@"

if [ "$MODE" = systemd ]; then
    write_unit
    echo
    echo "[install-startup] Enabling and starting the systemd user unit ${UNIT_NAME}..."
    systemctl --user daemon-reload
    systemctl --user enable --now "${UNIT_NAME}" >/dev/null
else
    write_desktop
    echo
    echo "[install-startup] No systemd user manager is running; registered the XDG autostart entry instead."
    echo "[install-startup] A desktop session starts it at graphical login (there is no restart on failure)."
    echo "[install-startup] Starting Armada.Server now..."
    "${COMMON_DIR}/start-armada-server.sh"
fi

"${COMMON_DIR}/healthcheck-server.sh"

echo
echo "[install-startup] Completed."
if [ "$MODE" = systemd ]; then
    echo "[install-startup] Unit file: ${UNIT_PATH}"
else
    echo "[install-startup] Autostart entry: ${DESKTOP_PATH}"
fi
