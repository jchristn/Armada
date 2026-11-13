#!/usr/bin/env bash
# Build and run Armada locally for development: builds the server and Harbor, builds and deploys the
# dashboard (deploy-dashboard.sh, into ~/.armada/dashboard where the server serves it from), starts the
# server in the background, waits for it to become healthy, then runs Harbor in the foreground. When Harbor
# exits (or you Ctrl+C), the background server is stopped too.
#
# Usage:
#   run-local.sh [-f <framework>|--framework <framework>|<framework>] [-k|--insecure]
# The framework (e.g. net8.0 or net10.0) is used to BOTH build and run; default net10.0.
# Set ARMADA_SKIP_DASHBOARD=1 to skip the dashboard build and keep the currently deployed dashboard.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }
case "${1:-}" in -h|--help) usage; exit 0 ;; esac

# shellcheck source=scripts/common/resolve-framework.sh
. "${SCRIPT_DIR}/resolve-framework.sh"
armada_resolve_framework "$@"
armada_reject_unexpected_args "$@"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
# Native-library checks (ICU, Avalonia, clipboard); they do nothing where there is no ldconfig (macOS).
# shellcheck source=scripts/common/native-libs.sh
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/native-libs.sh"
native_check_icu
# Harbor is a desktop (Avalonia) app: on Linux it needs a graphical session.
if [ "$(uname -s)" = Linux ] && [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    die "Armada Harbor is a desktop app and needs a graphical session, but neither DISPLAY nor WAYLAND_DISPLAY is set."
fi
if [ "$(uname -s)" = Linux ]; then
    native_check_libs error "Armada Harbor" "$NATIVE_AVALONIA_LIBS"
fi
FRAMEWORK="${ARMADA_TARGET_FRAMEWORK}"

BASE_URL="${ARMADA_BASE_URL:-http://localhost:7890}"
HEALTH_URL="${BASE_URL%/}/api/v1/status/health"

echo "[run-local] Building server + Harbor (framework ${FRAMEWORK})..."
dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" ${ARMADA_DOTNET_FRAMEWORK_ARGS}
dotnet build "${REPO_ROOT}/src/Armada.Harbor/Armada.Harbor.csproj" ${ARMADA_DOTNET_FRAMEWORK_ARGS}

if [ "${ARMADA_SKIP_DASHBOARD:-0}" = "1" ]; then
    echo "[run-local] Skipping dashboard build (ARMADA_SKIP_DASHBOARD=1)."
else
    "${SCRIPT_DIR}/deploy-dashboard.sh" "$@"
fi

echo "[run-local] Starting Armada Server (${FRAMEWORK})..."
dotnet run --project "${REPO_ROOT}/src/Armada.Server" --framework "${FRAMEWORK}" --no-build &
SERVER_PID=$!
trap 'echo "[run-local] Stopping server (pid ${SERVER_PID})..."; kill "${SERVER_PID}" 2>/dev/null || true' EXIT INT TERM

echo "[run-local] Waiting for server health at ${HEALTH_URL}..."
HEALTHY=0
for _ in $(seq 1 30); do
    if ! kill -0 "${SERVER_PID}" 2>/dev/null; then
        echo "[run-local] ERROR: server process exited before becoming healthy." >&2
        exit 1
    fi
    if command -v curl >/dev/null 2>&1 && curl -sf -o /dev/null "${HEALTH_URL}"; then
        HEALTHY=1
        break
    fi
    sleep 1
done

if [ "${HEALTHY}" -eq 1 ]; then
    echo "[run-local] Server healthy."
else
    echo "[run-local] WARNING: server not confirmed healthy after 30s; starting Harbor anyway (it reconnects)."
fi

echo "[run-local] Starting Armada Harbor (${FRAMEWORK})..."
dotnet run --project "${REPO_ROOT}/src/Armada.Harbor" --framework "${FRAMEWORK}" --no-build
