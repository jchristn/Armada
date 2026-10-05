#!/usr/bin/env bash
# =====================================================================
# in-container-package-test.sh -- runs INSIDE a clean distro container
# (started by verify-linux-package.sh). Installs the server package with
# the distro package manager, checks what it installed, runs
# "armada-server --install-service --dry-run", then starts the installed
# server and runs the REST smoke test against it. Everything (stub,
# server, git repo) lives inside the container; no ports are published.
#
# Usage (in the container): in-container-package-test.sh <package file>
# =====================================================================
set -uo pipefail
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

PKG="$1"
PASSWORD="Install-Verify-Container-Pw1"
FAIL=0
check() {
  if eval "$2"; then echo "PASS  $1"; else echo "FAIL  $1"; FAIL=1; fi
}

# Test prerequisites only (python3 runs the stub and the smoke test, git builds the origin repository).
# The package's own dependencies must come from the package metadata, not from this list.
iv_log "installing test prerequisites"
if command -v apt-get >/dev/null 2>&1; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq >/dev/null
  apt-get install -y -qq --no-install-recommends python3 ca-certificates >/dev/null || iv_die "apt-get failed"
  iv_log "apt-get install $PKG"
  apt-get install -y --no-install-recommends "$PKG" 2>&1 | tail -15
  INSTALL_CODE=${PIPESTATUS[0]}
  check "package installs with apt-get" "[ $INSTALL_CODE -eq 0 ]"
  dpkg -s armada-server 2>/dev/null | grep -E '^(Package|Version|Architecture|Depends):' | sed 's/^/      /'
  FILES="$(dpkg -L armada-server 2>/dev/null)"
else
  dnf install -y -q python3 >/dev/null || iv_die "dnf failed"
  iv_log "dnf install $PKG"
  dnf install -y "$PKG" 2>&1 | tail -15
  INSTALL_CODE=${PIPESTATUS[0]}
  check "package installs with dnf" "[ $INSTALL_CODE -eq 0 ]"
  rpm -qi armada-server 2>/dev/null | grep -E '^(Name|Version|Architecture)' | sed 's/^/      /'
  rpm -qR armada-server 2>/dev/null | grep -v '^rpmlib' | sed 's/^/      requires: /'
  FILES="$(rpm -ql armada-server 2>/dev/null)"
fi
[ "$INSTALL_CODE" -eq 0 ] || exit 1

check "/usr/bin/armada-server is on the PATH" "command -v armada-server >/dev/null"
TARGET="$(readlink -f /usr/bin/armada-server)"
check "/usr/bin/armada-server resolves into /usr/lib/armada-server ($TARGET)" "case '$TARGET' in /usr/lib/armada-server/*) [ -x '$TARGET' ] ;; *) false ;; esac"
check "package ships the React dashboard" "echo \"\$FILES\" | grep -q '/usr/lib/armada-server/dashboard/index.html'"
check "git is available to the server (package dependency)" "command -v git >/dev/null"

mkdir -p /tmp/iv/home
export HOME=/tmp/iv/home
export ARMADA_DATA_DIR=/tmp/iv/data
export ARMADA_INITIAL_ADMIN_PASSWORD="$PASSWORD"
cd /tmp/iv || exit 1

iv_log "armada-server --install-service --dry-run (root, so the system unit)"
armada-server --install-service --dry-run > /tmp/iv/dry-run.log 2>&1
DRY_CODE=$?
sed 's/^/      /' /tmp/iv/dry-run.log | head -40
check "--install-service --dry-run exits 0" "[ $DRY_CODE -eq 0 ]"
check "dry run describes armada.service with --run-service" "grep -q 'armada.service' /tmp/iv/dry-run.log && grep -q -- '--run-service' /tmp/iv/dry-run.log"
check "dry run wrote no unit file" "[ ! -e /etc/systemd/system/armada.service ]"

iv_write_settings "$ARMADA_DATA_DIR" 34040 34041
iv_make_origin /tmp/iv/origin.git
iv_start_stub 34052 /tmp/iv/stub.log

iv_log "starting armada-server"
armada-server > /tmp/iv/server.out 2>&1 &
IV_PIDS+=("$!")

iv_smoke --base-url "http://127.0.0.1:34040" --password "$PASSWORD" \
  --stub-url "http://127.0.0.1:34052/v1" --repo-url /tmp/iv/origin.git || FAIL=1

if [ "$FAIL" -ne 0 ]; then
  iv_log "server output (head and tail):"; head -15 /tmp/iv/server.out; echo "      ..."; tail -15 /tmp/iv/server.out
  iv_log "admiral log (warnings, errors, mission lines):"
  grep -hE "Warn|Error|Alert|Mission|Agent|Landing" /tmp/iv/data/logs/admiral.log* 2>/dev/null | tail -40 | cut -c1-400
fi
iv_stop_pids
exit "$FAIL"
