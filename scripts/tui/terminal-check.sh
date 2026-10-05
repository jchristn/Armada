#!/usr/bin/env bash
# Scripted terminal checks for `armada tui` (TUI_APP_PLAN.md W8.4): runs scripts/tui/terminal-check.py against a
# throwaway Admiral on this machine (pseudo-terminal matrix: UTF-8, LANG=C, TERM=dumb, NO_COLOR, an SSH-like session
# without a locale, 80x24, resize, keys, exit), then, when Docker is available, the same matrix plus tmux
# (TERM=screen-256color) and Linux console settings inside a Linux container with its own throwaway Admiral.
#
# Usage: scripts/tui/terminal-check.sh [--out DIR] [--port PORT] [--no-docker] [--no-build]
# Defaults: frames and results in a temp directory (printed), Admiral on 127.0.0.1:39110 (MCP 39111).
# Never touches ~/.armada; every process and container it starts is stopped by PID or name on exit.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
FRAMEWORK="net10.0"
PORT=39110
OUT=""
DOCKER=1
BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --no-docker) DOCKER=0; shift ;;
    --no-build) BUILD=0; shift ;;
    -h|--help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
MCP_PORT=$((PORT + 1))
[ -n "$OUT" ] || OUT="$(mktemp -d "${TMPDIR:-/tmp}/armada-tui-terminal-check.XXXXXX")"
mkdir -p "$OUT"
DATA_DIR="$(mktemp -d "${TMPDIR:-/tmp}/armada-tui-terminal-data.XXXXXX")"
TOKEN="tui-terminal-check-$(date +%s)"
CONTAINER="armada-tui-terminal-check-$$"
SERVER_PID=""

cleanup() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    for _ in $(seq 1 20); do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 0.5; done
    kill -9 "$SERVER_PID" 2>/dev/null || true
  fi
  if [ "$DOCKER" -eq 1 ]; then docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; fi
  rm -rf "$DATA_DIR"
}
trap cleanup EXIT

if [ "$BUILD" -eq 1 ]; then
  for p in Armada.Server Armada.Helm Armada.PerfSeed; do
    dotnet build "${REPO_ROOT}/src/${p}/${p}.csproj" -f "$FRAMEWORK" --nologo -v q >/dev/null
  done
fi
BIN_SERVER="${REPO_ROOT}/src/Armada.Server/bin/Debug/${FRAMEWORK}"
BIN_HELM="${REPO_ROOT}/src/Armada.Helm/bin/Debug/${FRAMEWORK}"
BIN_SEED="${REPO_ROOT}/src/Armada.PerfSeed/bin/Debug/${FRAMEWORK}"
SEED_ARGS=(--fleets 2 --vessels 6 --captains 2 --voyages 3 --missions 20 --jobs 5 --ask-threads 2)

if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then
  echo "ERROR: something already listens on 127.0.0.1:${PORT}; pick another --port." >&2
  exit 1
fi

echo "[terminal-check] seeding ${DATA_DIR} and starting the Admiral on 127.0.0.1:${PORT}"
dotnet "${BIN_SEED}/Armada.PerfSeed.dll" --data-dir "$DATA_DIR" --admiral-port "$PORT" --mcp-port "$MCP_PORT" --token "$TOKEN" "${SEED_ARGS[@]}" >/dev/null
ARMADA_DATA_DIR="$DATA_DIR" dotnet "${BIN_SERVER}/Armada.Server.dll" > "${DATA_DIR}/server-console.log" 2>&1 &
SERVER_PID=$!
for i in $(seq 1 120); do
  if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then tail -20 "${DATA_DIR}/server-console.log" >&2; exit 1; fi
  sleep 0.5
  if [ "$i" -eq 120 ]; then echo "ERROR: server not healthy after 60 s" >&2; exit 1; fi
done

STATUS=0
python3 "${SCRIPT_DIR}/terminal-check.py" --helm "${BIN_HELM}/Armada.Helm.dll" --server "http://127.0.0.1:${PORT}" \
  --token "$TOKEN" --out "$OUT" --platform "$(uname -s | tr '[:upper:]' '[:lower:]')" || STATUS=1

if [ "$DOCKER" -eq 1 ] && command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  echo "[terminal-check] Linux container ${CONTAINER} (tmux, Linux console settings)"
  docker run -d --name "$CONTAINER" -v "${REPO_ROOT}:/repo:ro" -v "${OUT}:/out" \
    "mcr.microsoft.com/dotnet/aspnet:10.0" sleep 3600 >/dev/null
  docker exec "$CONTAINER" bash -c "apt-get update -qq >/dev/null && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq tmux python3 python3-pyte curl locales >/dev/null"
  docker exec "$CONTAINER" bash -c "
    set -e
    mkdir -p /tmp/armada && cd /tmp
    dotnet /repo/src/Armada.PerfSeed/bin/Debug/${FRAMEWORK}/Armada.PerfSeed.dll --data-dir /tmp/armada --admiral-port 39120 --mcp-port 39121 --token '${TOKEN}' ${SEED_ARGS[*]} >/dev/null
    (ARMADA_DATA_DIR=/tmp/armada dotnet /repo/src/Armada.Server/bin/Debug/${FRAMEWORK}/Armada.Server.dll > /tmp/server.log 2>&1 &)
    for i in \$(seq 1 120); do curl -fsS http://127.0.0.1:39120/api/v1/status/health >/dev/null 2>&1 && break; sleep 0.5; done
    python3 /repo/scripts/tui/terminal-check.py --helm /repo/src/Armada.Helm/bin/Debug/${FRAMEWORK}/Armada.Helm.dll \
      --server http://127.0.0.1:39120 --token '${TOKEN}' --out /out --platform linux --tmux
  " || STATUS=1
else
  echo "[terminal-check] Docker not available or disabled: Linux and tmux checks skipped"
fi

echo "[terminal-check] frames and results: ${OUT}"
exit "$STATUS"
