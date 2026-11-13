#!/usr/bin/env bash
# =====================================================================
# run-baseline.sh -- the standard admission benchmark.
#
# For each candidate count (default 10 and 100) starts a fresh isolated
# Admiral (start-bench-server.sh), runs the Armada.Benchmark admission
# command with the same seeded inputs under Enforce, Observe and Disabled
# selection, stops the server and keeps the JSON report in
# benchmarks/results/. Exits non-zero when any run is incomparable (1) or
# breaks the p95 budget or accounting check (3).
#
# Usage:
#   run-baseline.sh [--candidates 10,100] [--missions 200] [--port 25160]
#                   [--stub-seconds 1] [--no-build] [-- <extra harness options>]
# =====================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
CANDIDATES="10,100"; MISSIONS=200; PORT=25160; STUB_SECONDS=1; BUILD_FLAG=""; EXTRA=()
while [ $# -gt 0 ]; do
  case "$1" in
    --candidates) CANDIDATES="$2"; shift 2 ;;
    --missions) MISSIONS="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --stub-seconds) STUB_SECONDS="$2"; shift 2 ;;
    --no-build) BUILD_FLAG="--no-build"; shift ;;
    --) shift; EXTRA=("$@"); break ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
if [ -z "$BUILD_FLAG" ]; then
  dotnet build "${REPO_ROOT}/src/Armada.Benchmark/Armada.Benchmark.csproj" -c Release --nologo -v q >/dev/null || exit 1
fi
STATUS=0
IFS=',' read -r -a COUNTS <<< "$CANDIDATES"
for count in "${COUNTS[@]}"; do
  echo "==> ${count} candidates, ${MISSIONS} missions per arm"
  OUT="$("${SCRIPT_DIR}/start-bench-server.sh" --name "admission-${count}" --port "$PORT" --stub-seconds "$STUB_SECONDS" ${BUILD_FLAG})" || { STATUS=1; continue; }
  BUILD_FLAG="--no-build"
  PID="$(echo "$OUT" | sed -n 's/^PID=//p')"; REPO_URL="$(echo "$OUT" | sed -n 's/^REPO_URL=//p')"
  dotnet "${REPO_ROOT}/src/Armada.Benchmark/bin/Release/net10.0/Armada.Benchmark.dll" admission \
    --url "http://127.0.0.1:${PORT}" --repo-url "$REPO_URL" --candidates "$count" --missions "$MISSIONS" \
    --output "${SCRIPT_DIR}/results/admission-${count}.json" "${EXTRA[@]+"${EXTRA[@]}"}"
  CODE=$?
  [ "$CODE" -gt "$STATUS" ] && STATUS=$CODE
  kill "$PID" 2>/dev/null; for _ in $(seq 1 20); do kill -0 "$PID" 2>/dev/null || break; sleep 0.5; done; kill -9 "$PID" 2>/dev/null || true
done
exit $STATUS
