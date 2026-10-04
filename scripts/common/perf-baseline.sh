#!/usr/bin/env bash
# =====================================================================
# perf-baseline.sh -- repeatable REST performance baseline (V1_READINESS W4.5).
#
# Seeds a throwaway data directory with src/Armada.PerfSeed (default: 500
# vessels with health rows, 10,000 missions, 1,000 voyages, 50 captains,
# 2,000 jobs, 300 Ask threads), starts a Release build of the Admiral on
# 127.0.0.1 with that data (ARMADA_DATA_DIR; ~/.armada is never touched),
# measures p50/p95 of the endpoints in scripts/common/perf-measure.py, then
# stops the server and deletes the data unless --keep is given.
#
# Usage:
#   perf-baseline.sh [--framework net10.0] [--port 25060] [--runs 50]
#                    [--warmup 5] [--output results.tsv] [--keep]
#                    [--no-build] [-- <extra Armada.PerfSeed options>]
#
# Requirements: dotnet, python3, curl. The MCP port is --port + 1.
# Results are recorded in docs/PERFORMANCE.md.
# =====================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

FRAMEWORK="net10.0"
PORT=25060
RUNS=50
WARMUP=5
OUTPUT=""
KEEP=0
DO_BUILD=1
SEED_ARGS=()

while [ $# -gt 0 ]; do
  case "$1" in
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --runs) RUNS="$2"; shift 2 ;;
    --warmup) WARMUP="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    --no-build) DO_BUILD=0; shift ;;
    --) shift; SEED_ARGS=("$@"); break ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

for tool in dotnet python3 curl; do
  command -v "$tool" >/dev/null 2>&1 || { echo "ERROR: $tool is required." >&2; exit 1; }
done

MCP_PORT=$((PORT + 1))
TOKEN="perf-baseline-$(date +%s)"
DATA_DIR="$(mktemp -d "${TMPDIR:-/tmp}/armada-perf.XXXXXX")"
[ -n "$OUTPUT" ] || OUTPUT="${DATA_DIR}/results.tsv"
SERVER_PID=""

cleanup() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true
    for _ in $(seq 1 20); do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 0.5; done
    kill -9 "$SERVER_PID" 2>/dev/null || true
  fi
  if [ "$KEEP" -eq 0 ]; then rm -rf "$DATA_DIR"; else echo "[perf] kept data directory: $DATA_DIR"; fi
}
trap cleanup EXIT

if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then
  echo "ERROR: something is already listening on 127.0.0.1:${PORT}; pick another --port." >&2
  exit 1
fi

if [ "$DO_BUILD" -eq 1 ]; then
  echo "[perf] building Release (${FRAMEWORK})"
  dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" -c Release -f "$FRAMEWORK" --nologo -v q >/dev/null
  dotnet build "${REPO_ROOT}/src/Armada.PerfSeed/Armada.PerfSeed.csproj" -c Release -f "$FRAMEWORK" --nologo -v q >/dev/null
fi

echo "[perf] seeding ${DATA_DIR}"
dotnet "${REPO_ROOT}/src/Armada.PerfSeed/bin/Release/${FRAMEWORK}/Armada.PerfSeed.dll" \
  --data-dir "$DATA_DIR" --admiral-port "$PORT" --mcp-port "$MCP_PORT" --token "$TOKEN" "${SEED_ARGS[@]+"${SEED_ARGS[@]}"}"

echo "[perf] starting Admiral on 127.0.0.1:${PORT}"
ARMADA_DATA_DIR="$DATA_DIR" dotnet "${REPO_ROOT}/src/Armada.Server/bin/Release/${FRAMEWORK}/Armada.Server.dll" \
  > "${DATA_DIR}/server-console.log" 2>&1 &
SERVER_PID=$!

for i in $(seq 1 120); do
  if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then echo "ERROR: server exited; see ${DATA_DIR}/server-console.log" >&2; tail -20 "${DATA_DIR}/server-console.log" >&2; exit 1; fi
  sleep 0.5
  if [ "$i" -eq 120 ]; then echo "ERROR: server not healthy after 60 s" >&2; exit 1; fi
done

echo "[perf] machine: $(uname -sm), $(getconf _NPROCESSORS_ONLN 2>/dev/null || echo '?') cpus, load: $(uptime | sed 's/.*load average[s]*: //')"
echo "[perf] measuring: ${WARMUP} warmup + ${RUNS} measured requests per endpoint"
python3 "${SCRIPT_DIR}/perf-measure.py" "http://127.0.0.1:${PORT}" "$TOKEN" "$RUNS" "$WARMUP" "$OUTPUT"
echo "[perf] TSV results: ${OUTPUT}"
