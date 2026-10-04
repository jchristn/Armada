#!/usr/bin/env bash
# =====================================================================
# run-db-parity-tests.sh -- run the full Database test suite against every
# supported provider (SQLite, PostgreSQL, MySQL, SQL Server) to verify
# cross-provider parity.
#
# SQLite runs in-process. Each server provider is exercised against a
# throwaway Docker container on a random host port; the container is torn
# down afterward. The identical Touchstone "Database" suite runs against
# every provider, so a green run proves the schema and every driver's
# DB-method implementations behave the same everywhere.
#
# Usage:
#   run-db-parity-tests.sh [--providers sqlite,postgresql,mysql,sqlserver]
#                          [--framework net10.0] [--no-build]
#
# Requirements: docker, dotnet. Exits non-zero if any provider has a
# failing test.
# =====================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

PROVIDERS="sqlite,postgresql,mysql,sqlserver"
FRAMEWORK="net10.0"
DO_BUILD=1

while [ $# -gt 0 ]; do
  case "$1" in
    --providers) PROVIDERS="$2"; shift 2 ;;
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --no-build) DO_BUILD=0; shift ;;
    -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

MYSQL_IMAGE="${ARMADA_MYSQL_IMAGE:-mysql:8.4}"
POSTGRES_IMAGE="${ARMADA_POSTGRES_IMAGE:-postgres:17-alpine}"
SQLSERVER_IMAGE="${ARMADA_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
SA_PASSWORD="${ARMADA_SQLSERVER_SA_PASSWORD:-Str0ng!Passw0rd2024}"

TEST_PROJECT="${REPO_ROOT}/src/Test.Automated"
RESULT_DIR="$(mktemp -d)"
CONTAINERS=()

cleanup() {
  for c in "${CONTAINERS[@]:-}"; do
    [ -n "$c" ] && docker rm -f "$c" >/dev/null 2>&1 || true
  done
  rm -rf "$RESULT_DIR" 2>/dev/null || true
}
trap cleanup EXIT

rand_port() { echo $(( (RANDOM % 26000) + 33000 )); }

# run_suite <label> <extra dotnet args...> -- runs the Database suite, prints the
# Total line, and returns the suite's exit code.
run_suite() {
  local label="$1"; shift
  local out="${RESULT_DIR}/${label}.txt"
  ( cd "$REPO_ROOT" && dotnet run --project "$TEST_PROJECT" --framework "$FRAMEWORK" -c Debug ${DOTNET_NO_BUILD} -- \
      --suites Database "$@" ) > "$out" 2>&1
  local code=$?
  local total
  total="$(grep -E '^Total:' "$out" | tail -1)"
  if [ -z "$total" ]; then
    echo "  ${label}: NO RESULT (see below)"; tail -20 "$out"
  else
    echo "  ${label}: ${total}"
  fi
  SUMMARY+=("${label} :: ${total:-NO RESULT}")
  return $code
}

wait_ready() {
  local name="$1"; shift
  local i
  for i in $(seq 1 90); do
    if docker exec "$name" "$@" >/dev/null 2>&1; then return 0; fi
    sleep 2
  done
  return 1
}

DOTNET_NO_BUILD=""
if [ "$DO_BUILD" -eq 1 ]; then
  echo "==> Building test project (${FRAMEWORK})"
  ( cd "$REPO_ROOT" && dotnet build "$TEST_PROJECT" --framework "$FRAMEWORK" -c Debug --nologo -v q ) || { echo "Build failed"; exit 1; }
fi
DOTNET_NO_BUILD="--no-build"

SUMMARY=()
FAIL=0

IFS=',' read -ra PROVIDER_LIST <<< "$PROVIDERS"
for provider in "${PROVIDER_LIST[@]}"; do
  provider="$(echo "$provider" | tr '[:upper:]' '[:lower:]' | xargs)"
  case "$provider" in
    sqlite)
      echo "==> SQLite (in-process)"
      run_suite "sqlite" || FAIL=1
      ;;
    postgresql|postgres|pg)
      echo "==> PostgreSQL (${POSTGRES_IMAGE})"
      port="$(rand_port)"; name="armada_parity_pg_$$"
      CONTAINERS+=("$name")
      docker run -d --name "$name" --shm-size=256m -e POSTGRES_PASSWORD=testpass -p "${port}:5432" "$POSTGRES_IMAGE" >/dev/null
      if wait_ready "$name" pg_isready -U postgres -q; then
        run_suite "postgresql" --db-type postgresql --db-host 127.0.0.1 --db-port "$port" --db-user postgres --db-pass testpass --db-name armada_test || FAIL=1
      else echo "  postgresql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    mysql|mariadb)
      echo "==> MySQL (${MYSQL_IMAGE})"
      port="$(rand_port)"; name="armada_parity_my_$$"
      CONTAINERS+=("$name")
      # skip-name-resolve avoids a slow reverse-DNS on every connect; durability
      # is disabled because the container is a throwaway.
      docker run -d --name "$name" -e MYSQL_ROOT_PASSWORD=testpass -p "${port}:3306" "$MYSQL_IMAGE" \
        --skip-name-resolve --innodb-flush-log-at-trx-commit=0 --sync-binlog=0 >/dev/null
      if wait_ready "$name" mysqladmin ping -uroot -ptestpass --silent; then
        sleep 3
        run_suite "mysql" --db-type mysql --db-host 127.0.0.1 --db-port "$port" --db-user root --db-pass testpass --db-name armada_test || FAIL=1
      else echo "  mysql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    sqlserver|mssql)
      echo "==> SQL Server (${SQLSERVER_IMAGE})"
      port="$(rand_port)"; name="armada_parity_ss_$$"
      CONTAINERS+=("$name")
      docker run -d --name "$name" -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=${SA_PASSWORD}" -p "${port}:1433" "$SQLSERVER_IMAGE" >/dev/null
      if wait_ready "$name" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" -b; then
        run_suite "sqlserver" --db-type sqlserver --db-host 127.0.0.1 --db-port "$port" --db-user sa --db-pass "$SA_PASSWORD" --db-name armada_test || FAIL=1
      else echo "  sqlserver: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    *) echo "Unknown provider: $provider" >&2; FAIL=1 ;;
  esac
done

echo ""
echo "===================== DB PARITY SUMMARY ====================="
for line in "${SUMMARY[@]:-}"; do echo "  $line"; done
echo "============================================================"
if [ "$FAIL" -ne 0 ]; then echo "RESULT: FAILURES DETECTED"; exit 1; fi
echo "RESULT: PARITY OK"
