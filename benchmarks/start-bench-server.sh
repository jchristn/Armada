#!/usr/bin/env bash
# =====================================================================
# start-bench-server.sh -- run the Admiral from the working tree for the
# benchmark, isolated under benchmarks/.run/<name>.
#
# Builds Armada.Server in Release (unless --no-build), regenerates
# settings.json from benchmarks/config/settings.json on every start, creates
# a local bare git repository for benchmark vessels, puts the stub agent
# (benchmarks/stub/agent/claude) first on PATH, and starts the Admiral on
# 127.0.0.1:<port> (default 25160; MCP is port + 1). Prints the PID, data
# directory and repository URL as KEY=VALUE lines, then returns once the
# server is healthy. Stop it with: kill <PID>.
#
# Usage:
#   start-bench-server.sh [--name admission] [--port 25160] [--framework net10.0]
#                         [--stub-seconds 1] [--no-build]
#
# Port block 25160-25169 is reserved for this suite. Your ~/.armada and the
# default ports 7890/7891 are never touched.
# =====================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
NAME="admission"; PORT=25160; FRAMEWORK="net10.0"; STUB_SECONDS=1; DO_BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --name) NAME="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --stub-seconds) STUB_SECONDS="$2"; shift 2 ;;
    --no-build) DO_BUILD=0; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
for tool in dotnet python3 curl git; do command -v "$tool" >/dev/null 2>&1 || { echo "ERROR: $tool is required." >&2; exit 1; }; done
if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then
  echo "ERROR: something is already listening on 127.0.0.1:${PORT}." >&2; exit 1
fi
if [ "$DO_BUILD" -eq 1 ]; then
  dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" -c Release -f "$FRAMEWORK" -p:TargetFramework="$FRAMEWORK" -p:TargetFrameworks="$FRAMEWORK" --nologo -v q >/dev/null
fi

DATA_DIR="${SCRIPT_DIR}/.run/${NAME}"
rm -rf "$DATA_DIR"; mkdir -p "$DATA_DIR"
sed -e "s|__DATA_DIR__|${DATA_DIR}|g" -e "s|__PORT__|${PORT}|g" -e "s|__MCP_PORT__|$((PORT + 1))|g" \
  "${SCRIPT_DIR}/config/settings.json" > "${DATA_DIR}/settings.json"

# A one-commit bare repository every benchmark vessel clones.
WORK="${DATA_DIR}/seed-repo"; BARE="${DATA_DIR}/bench-repo.git"
git init -q -b main "$WORK"
printf '# Benchmark repository\n' > "${WORK}/README.md"
git -C "$WORK" -c user.name=bench -c user.email=bench@example.invalid add README.md
git -C "$WORK" -c user.name=bench -c user.email=bench@example.invalid commit -q -m "Benchmark seed"
git clone -q --bare "$WORK" "$BARE"

ARMADA_DATA_DIR="$DATA_DIR" ARMADA_BENCH_STUB_SECONDS="$STUB_SECONDS" PATH="${SCRIPT_DIR}/stub/agent:${PATH}" \
  nohup dotnet "${REPO_ROOT}/src/Armada.Server/bin/Release/${FRAMEWORK}/Armada.Server.dll" > "${DATA_DIR}/server-console.log" 2>&1 &
PID=$!
for i in $(seq 1 120); do
  if curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$PID" 2>/dev/null; then echo "ERROR: server exited; see ${DATA_DIR}/server-console.log" >&2; tail -20 "${DATA_DIR}/server-console.log" >&2; exit 1; fi
  sleep 0.5
  if [ "$i" -eq 120 ]; then echo "ERROR: server not healthy after 60 s" >&2; kill "$PID"; exit 1; fi
done
echo "PID=${PID}"
echo "DATA_DIR=${DATA_DIR}"
echo "REPO_URL=file://${BARE}"
