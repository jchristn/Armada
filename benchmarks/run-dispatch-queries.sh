#!/usr/bin/env bash
# =====================================================================
# run-dispatch-queries.sh -- database statements per dispatch pass (G11).
#
# Starts a throwaway PostgreSQL container with pg_stat_statements, runs the
# Admiral from this working tree against it with one captain (concurrency 1)
# whose stub agent runs for 10 minutes (after captain validation), and submits 51 missions through the
# admission runner. One mission runs, 50 (49 if the first one finished
# during captain validation timing) stay Pending. After the queue
# settles the harness is stopped (its polling is not counted), the
# statement counters are reset, sampled for --window seconds and written to
# results/dispatch-queries.json:
#
#   totalCalls        every statement the Admiral ran in the window
#   protocolCalls     DISCARD ALL (pool reset), BEGIN and COMMIT
#   queryCalls        totalCalls minus protocolCalls
#   missionCalls      statements that read or write the missions table
#   pendingReads      statements that select Pending missions (one per
#                     dispatch pass in 1.5.0)
#   top               the 25 most frequent normalized statements
#
# The container and server are removed afterwards. Nothing leaves
# 127.0.0.1. Ports: --port (Admiral, +1 MCP, +2 metrics), --pg-port.
#
# Usage:
#   run-dispatch-queries.sh [--port 25163] [--pg-port 25439] [--window 60]
#                           [--settle 20] [--output results/dispatch-queries.json]
# =====================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PORT=25163; PG_PORT=25439; WINDOW=60; SETTLE=20; OUTPUT="${SCRIPT_DIR}/results/dispatch-queries.json"
while [ $# -gt 0 ]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --pg-port) PG_PORT="$2"; shift 2 ;;
    --window) WINDOW="$2"; shift 2 ;;
    --settle) SETTLE="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
CONTAINER="armada-bench-dispatch-pg"
PSQL=(docker exec -i "$CONTAINER" psql -U postgres -d armada_bench -At -F $'\t')

cleanup() {
  [ -n "${HARNESS_PID:-}" ] && kill "$HARNESS_PID" 2>/dev/null
  if [ -n "${SERVER_PID:-}" ]; then
    kill "$SERVER_PID" 2>/dev/null; for _ in $(seq 1 20); do kill -0 "$SERVER_PID" 2>/dev/null || break; sleep 0.5; done
    kill -9 "$SERVER_PID" 2>/dev/null || true
  fi
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" -e POSTGRES_PASSWORD=bench -e POSTGRES_DB=armada_bench -p "127.0.0.1:${PG_PORT}:5432" \
  postgres:16 -c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=all -c pg_stat_statements.max=10000 >/dev/null || exit 1
for i in $(seq 1 60); do docker exec "$CONTAINER" pg_isready -U postgres -d armada_bench >/dev/null 2>&1 && break; sleep 1; done
sleep 2
"${PSQL[@]}" -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;" >/dev/null || exit 1

dotnet build "${REPO_ROOT}/src/Armada.Server/Armada.Server.csproj" -c Release -f net10.0 -p:TargetFramework=net10.0 -p:TargetFrameworks=net10.0 --nologo -v q >/dev/null || exit 1
dotnet build "${REPO_ROOT}/src/Armada.Benchmark/Armada.Benchmark.csproj" -c Release --nologo -v q >/dev/null || exit 1

DATA_DIR="${SCRIPT_DIR}/.run/dispatch-queries"
rm -rf "$DATA_DIR"; mkdir -p "$DATA_DIR"
sed -e "s|__DATA_DIR__|${DATA_DIR}|g" -e "s|__PORT__|${PORT}|g" -e "s|__MCP_PORT__|$((PORT + 1))|g" \
  -e "s|__TELEMETRY__|false|g" -e "s|__METRICS_PORT__|$((PORT + 2))|g" \
  -e "s|\"database\": { \"type\": \"Sqlite\", \"filename\": \"[^\"]*\" }|\"database\": { \"type\": \"Postgresql\", \"hostname\": \"127.0.0.1\", \"port\": ${PG_PORT}, \"username\": \"postgres\", \"password\": \"bench\", \"databaseName\": \"armada_bench\" }|" \
  "${SCRIPT_DIR}/config/settings.json" > "${DATA_DIR}/settings.json"
grep -q Postgresql "${DATA_DIR}/settings.json" || { echo "ERROR: could not point settings at PostgreSQL" >&2; exit 1; }

WORK="${DATA_DIR}/seed-repo"; BARE="${DATA_DIR}/bench-repo.git"
git init -q -b main "$WORK"
printf '# Benchmark repository\n' > "${WORK}/README.md"
git -C "$WORK" -c user.name=bench -c user.email=bench@example.invalid add README.md
git -C "$WORK" -c user.name=bench -c user.email=bench@example.invalid commit -q -m "Benchmark seed"
git clone -q --bare "$WORK" "$BARE"

# Captain creation validates the model by running the agent once, so the stub stays short until the
# first mission exists; then the wrapper makes every later agent run last 10 minutes.
WRAP="${DATA_DIR}/agent"; mkdir -p "$WRAP"
for agent in "${SCRIPT_DIR}"/stub/agent/*; do
  printf '#!/usr/bin/env bash\n[ -f "%s/long" ] && export ARMADA_BENCH_STUB_SECONDS=600\nexec "%s" "$@"\n' "$DATA_DIR" "$agent" > "${WRAP}/$(basename "$agent")"
  chmod +x "${WRAP}/$(basename "$agent")"
done
ARMADA_DATA_DIR="$DATA_DIR" ARMADA_BENCH_STUB_SECONDS=1 PATH="${WRAP}:${PATH}" \
  nohup dotnet "${REPO_ROOT}/src/Armada.Server/bin/Release/net10.0/Armada.Server.dll" > "${DATA_DIR}/server-console.log" 2>&1 &
SERVER_PID=$!
for i in $(seq 1 120); do
  curl -fsS "http://127.0.0.1:${PORT}/api/v1/status/health" >/dev/null 2>&1 && break
  kill -0 "$SERVER_PID" 2>/dev/null || { echo "ERROR: server exited" >&2; tail -20 "${DATA_DIR}/server-console.log" >&2; exit 1; }
  sleep 0.5
done

dotnet "${REPO_ROOT}/src/Armada.Benchmark/bin/Release/net10.0/Armada.Benchmark.dll" admission \
  --url "http://127.0.0.1:${PORT}" --repo-url "file://${BARE}" --candidates 1 --captain-concurrency 1 --missions 51 \
  --arms Disabled --warmup 0 --arm-timeout 3600 --output "${DATA_DIR}/admission.json" > "${DATA_DIR}/harness.log" 2>&1 &
HARNESS_PID=$!

for i in $(seq 1 240); do
  [ "$("${PSQL[@]}" -c "SELECT count(*) FROM missions;" 2>/dev/null || echo 0)" -ge 1 ] 2>/dev/null && break
  kill -0 "$HARNESS_PID" 2>/dev/null || { echo "ERROR: harness exited" >&2; tail -5 "${DATA_DIR}/harness.log" >&2; exit 1; }
  sleep 0.5
done
touch "${DATA_DIR}/long"

# Wait until 51 missions exist and one is running, then let the queue settle.
for i in $(seq 1 240); do
  COUNTS="$("${PSQL[@]}" -c "SELECT count(*), count(*) FILTER (WHERE status = 'Pending') FROM missions;" 2>/dev/null)"
  TOTAL="$(echo "$COUNTS" | cut -f1)"; PENDING="$(echo "$COUNTS" | cut -f2)"
  [ "${TOTAL:-0}" -ge 51 ] && [ "${PENDING:-0}" -ge 49 ] && break
  sleep 1
done
[ "${PENDING:-0}" -ge 49 ] || { echo "ERROR: expected 50 Pending missions, saw ${PENDING:-0} of ${TOTAL:-0}" >&2; tail -5 "${DATA_DIR}/harness.log" >&2; exit 1; }
# Stop the harness so its polling (mission and usage reads) is not counted; the missions stay Pending.
kill "$HARNESS_PID" 2>/dev/null; HARNESS_PID=""
sleep "$SETTLE"

"${PSQL[@]}" -c "SELECT pg_stat_statements_reset();" >/dev/null
sleep "$WINDOW"
mkdir -p "$(dirname "$OUTPUT")"
"${PSQL[@]}" -c "SELECT calls, regexp_replace(query, '\s+', ' ', 'g') FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = 'armada_bench') AND query NOT ILIKE '%pg_stat_statements%' ORDER BY calls DESC;" > "${DATA_DIR}/statements.tsv"
PENDING_NOW="$("${PSQL[@]}" -c "SELECT count(*) FROM missions WHERE status = 'Pending';")"

python3 - "$DATA_DIR/statements.tsv" "$OUTPUT" "$WINDOW" "$PENDING_NOW" "$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)" <<'PY'
import json, re, sys
rows = []
with open(sys.argv[1]) as f:
    for line in f:
        parts = line.rstrip("\n").split("\t", 1)
        if len(parts) == 2 and parts[0].isdigit():
            rows.append((int(parts[0]), parts[1]))
def touches_missions(q):
    return re.search(r"\b(from|update|into|join)\s+\"?missions\"?\b", q, re.I) is not None
def pending_read(q):
    return touches_missions(q) and q.lower().lstrip().startswith("select") and re.search(r"status", q, re.I) is not None and ("Pending" in q or "$" in q)
protocol = sum(c for c, q in rows if q.strip().upper() in ("DISCARD ALL", "COMMIT") or q.strip().upper().startswith("BEGIN"))
total = sum(c for c, _ in rows)
queries = total - protocol
report = {
    "commit": sys.argv[5],
    "windowSeconds": int(sys.argv[3]),
    "pendingMissions": int(sys.argv[4] or 0),
    "totalCalls": total,
    "protocolCalls": protocol,
    "queryCalls": queries,
    "queriesPerMinute": round(queries * 60.0 / int(sys.argv[3]), 1),
    "missionCalls": sum(c for c, q in rows if touches_missions(q)),
    "pendingReads": sum(c for c, q in rows if pending_read(q)),
    "distinctStatements": len(rows),
    "top": [{"calls": c, "query": q[:300]} for c, q in rows[:25]],
}
with open(sys.argv[2], "w") as f:
    json.dump(report, f, indent=2)
print(json.dumps({k: v for k, v in report.items() if k != "top"}))
PY
