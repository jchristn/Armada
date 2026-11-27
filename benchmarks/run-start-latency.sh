#!/usr/bin/env bash
# =====================================================================
# run-start-latency.sh -- Pending to agent process started with free
# capacity (G10).
#
# Starts a fresh isolated Admiral (start-bench-server.sh) and runs the
# admission runner with 10 captains (4 concurrent missions each) and 30
# missions submitted one at a time, 3 seconds apart, so every mission finds
# a free captain. The report (results/start-latency.json) carries
# startLatency: CreatedUtc to StartedUtc per mission. The burst runs of
# run-baseline.sh measure queueing behind 200 launches instead.
#
# Usage:
#   run-start-latency.sh [--port 25163] [--missions 30] [--interval-ms 3000]
#                        [--no-build]
# =====================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PORT=25163; MISSIONS=30; INTERVAL=3000; BUILD_FLAG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --missions) MISSIONS="$2"; shift 2 ;;
    --interval-ms) INTERVAL="$2"; shift 2 ;;
    --no-build) BUILD_FLAG="--no-build"; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [ -z "$BUILD_FLAG" ]; then
  dotnet build "${REPO_ROOT}/src/Armada.Benchmark/Armada.Benchmark.csproj" -c Release --nologo -v q >/dev/null || exit 1
fi
OUT="$("${SCRIPT_DIR}/start-bench-server.sh" --name start-latency --port "$PORT" --stub-seconds 1 ${BUILD_FLAG})" || exit 1
PID="$(echo "$OUT" | sed -n 's/^PID=//p')"; REPO_URL="$(echo "$OUT" | sed -n 's/^REPO_URL=//p')"
dotnet "${REPO_ROOT}/src/Armada.Benchmark/bin/Release/net10.0/Armada.Benchmark.dll" admission \
  --url "http://127.0.0.1:${PORT}" --repo-url "$REPO_URL" --candidates 10 --missions "$MISSIONS" \
  --arms Disabled --warmup 5 --submit-concurrency 1 --submit-interval-ms "$INTERVAL" \
  --output "${SCRIPT_DIR}/results/start-latency.json"
CODE=$?
kill "$PID" 2>/dev/null; for _ in $(seq 1 20); do kill -0 "$PID" 2>/dev/null || break; sleep 0.5; done; kill -9 "$PID" 2>/dev/null || true
exit $CODE
