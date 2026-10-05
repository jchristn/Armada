#!/usr/bin/env bash
# =====================================================================
# run-upgrade-test.sh -- upgrade test (V1 readiness W3.1): seed data on an
# older Armada (default: v0.9.0), then start the current build on the same
# database and verify everything survived the migration.
#
# Steps:
#   1. Extract the baseline source (git archive <ref> src) into a temp dir,
#      add scripts/common/upgrade-baseline-host to it, and build the host.
#   2. Build src/Test.Automated from this checkout.
#   3. For each provider, run the "Upgrade" suite (Test.Shared
#      UpgradeFromBaselineSuite). SQLite runs against a temp file; PostgreSQL,
#      MySQL, and SQL Server each get a throwaway Docker container.
#
# The baseline never reads or writes ~/.armada: it gets explicit settings,
# and its HOME / USERPROFILE / ARMADA_DATA_DIR point into the temp dir.
#
# Usage:
#   run-upgrade-test.sh [--from-ref <git ref>] [--providers sqlite,postgresql,mysql,sqlserver|all]
#                       [--framework net10.0] [--keep]
#
# Defaults: --from-ref is the v0.9.0 tag when it exists, otherwise the
# "release(v0.9.0)" commit e456b0080e5a834d5469cd615d73b3d542b00431;
# --providers sqlite. Set ARMADA_UPGRADE_DB_PORT_BASE to pin the container
# host ports (base+0..2) instead of random ones.
#
# Requirements: git, dotnet, and docker for the server providers. Exits
# non-zero if any provider fails.
# =====================================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

RELEASE_COMMIT="e456b0080e5a834d5469cd615d73b3d542b00431"
FROM_REF=""
PROVIDERS="sqlite"
FRAMEWORK="net10.0"
KEEP=0

while [ $# -gt 0 ]; do
  case "$1" in
    --from-ref) FROM_REF="$2"; shift 2 ;;
    --providers) PROVIDERS="$2"; shift 2 ;;
    --framework) FRAMEWORK="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ "$PROVIDERS" = "all" ]; then PROVIDERS="sqlite,postgresql,mysql,sqlserver"; fi

if [ -z "$FROM_REF" ]; then
  if git -C "$REPO_ROOT" rev-parse -q --verify "refs/tags/v0.9.0" >/dev/null 2>&1; then
    FROM_REF="v0.9.0"
  else
    FROM_REF="$RELEASE_COMMIT"
  fi
fi

RESOLVED="$(git -C "$REPO_ROOT" rev-parse --verify "${FROM_REF}^{commit}" 2>/dev/null)" || { echo "Unknown git ref: $FROM_REF" >&2; exit 2; }
SHORT="$(git -C "$REPO_ROOT" rev-parse --short "$RESOLVED")"

MYSQL_IMAGE="${ARMADA_MYSQL_IMAGE:-mysql:8.4}"
POSTGRES_IMAGE="${ARMADA_POSTGRES_IMAGE:-postgres:17-alpine}"
SQLSERVER_IMAGE="${ARMADA_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
SA_PASSWORD="${ARMADA_SQLSERVER_SA_PASSWORD:-Str0ng!Passw0rd2024}"

WORK="$(mktemp -d)"
CONTAINERS=()
cleanup() {
  for c in "${CONTAINERS[@]:-}"; do
    [ -n "$c" ] && docker rm -f "$c" >/dev/null 2>&1 || true
  done
  if [ "$KEEP" -eq 0 ]; then rm -rf "$WORK" 2>/dev/null || true; else echo "Work directory kept: $WORK"; fi
}
trap cleanup EXIT

echo "==> Baseline: ${FROM_REF} (${SHORT})"
mkdir -p "$WORK/baseline" "$WORK/runner-home"
git -C "$REPO_ROOT" archive "$RESOLVED" src | tar -x -C "$WORK/baseline" || { echo "git archive failed"; exit 1; }
mkdir -p "$WORK/baseline/src/UpgradeBaselineHost"
cp "$SCRIPT_DIR/upgrade-baseline-host/UpgradeBaselineHost.csproj" "$SCRIPT_DIR/upgrade-baseline-host/Program.cs" "$WORK/baseline/src/UpgradeBaselineHost/"

echo "==> Building baseline host (${FRAMEWORK})"
( cd "$WORK/baseline/src" && dotnet build UpgradeBaselineHost/UpgradeBaselineHost.csproj -c Release -f "$FRAMEWORK" -o "$WORK/baseline-bin" --nologo -v q ) \
  > "$WORK/baseline-build.log" 2>&1 || { echo "Baseline build failed:"; tail -40 "$WORK/baseline-build.log"; exit 1; }

echo "==> Building test runner (${FRAMEWORK})"
( cd "$REPO_ROOT" && dotnet build src/Test.Automated --framework "$FRAMEWORK" -c Debug --nologo -v q ) \
  > "$WORK/runner-build.log" 2>&1 || { echo "Test runner build failed:"; tail -40 "$WORK/runner-build.log"; exit 1; }

export ARMADA_UPGRADE_BASELINE_HOST="$WORK/baseline-bin/UpgradeBaselineHost.dll"
export ARMADA_UPGRADE_BASELINE_REF="${FROM_REF} (${SHORT})"
# Anything that falls back to the default data directory lands in the temp dir, never ~/.armada.
export ARMADA_DATA_DIR="$WORK/runner-home/.armada"

rand_port() { echo $(( (RANDOM % 26000) + 33000 )); }
port_for() {
  if [ -n "${ARMADA_UPGRADE_DB_PORT_BASE:-}" ]; then echo $(( ARMADA_UPGRADE_DB_PORT_BASE + $1 )); else rand_port; fi
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

SUMMARY=()
FAIL=0
run_suite() {
  local label="$1"; shift
  local out="$WORK/${label}.txt"
  ( cd "$REPO_ROOT" && dotnet run --project src/Test.Automated --framework "$FRAMEWORK" -c Debug --no-build -- --suites Upgrade "$@" ) > "$out" 2>&1
  local code=$?
  local total
  total="$(grep -E '^Total:' "$out" | tail -1)"
  if [ -z "$total" ] || [ $code -ne 0 ]; then
    echo "  ${label}: ${total:-NO RESULT}"; grep -E 'FAIL|Assertion|Exception' "$out" | head -20
  else
    echo "  ${label}: ${total}"
  fi
  SUMMARY+=("${label} :: ${total:-NO RESULT}")
  return $code
}

IFS=',' read -ra PROVIDER_LIST <<< "$PROVIDERS"
for provider in "${PROVIDER_LIST[@]}"; do
  provider="$(echo "$provider" | tr '[:upper:]' '[:lower:]' | xargs)"
  case "$provider" in
    sqlite)
      echo "==> SQLite"
      run_suite "sqlite" || FAIL=1
      ;;
    postgresql|postgres|pg)
      echo "==> PostgreSQL (${POSTGRES_IMAGE})"
      port="$(port_for 0)"; name="armada_upgrade_pg_$$"
      CONTAINERS+=("$name")
      docker run -d --name "$name" --shm-size=256m -e POSTGRES_PASSWORD=testpass -p "${port}:5432" "$POSTGRES_IMAGE" >/dev/null
      if wait_ready "$name" pg_isready -U postgres -q; then
        sleep 2
        run_suite "postgresql" --db-type postgresql --db-host 127.0.0.1 --db-port "$port" --db-user postgres --db-pass testpass --db-name armada_upgrade || FAIL=1
      else echo "  postgresql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    mysql|mariadb)
      echo "==> MySQL (${MYSQL_IMAGE})"
      port="$(port_for 1)"; name="armada_upgrade_my_$$"
      CONTAINERS+=("$name")
      docker run -d --name "$name" -e MYSQL_ROOT_PASSWORD=testpass -p "${port}:3306" "$MYSQL_IMAGE" \
        --skip-name-resolve --innodb-flush-log-at-trx-commit=0 --sync-binlog=0 >/dev/null
      if wait_ready "$name" mysqladmin ping -uroot -ptestpass --silent; then
        sleep 3
        run_suite "mysql" --db-type mysql --db-host 127.0.0.1 --db-port "$port" --db-user root --db-pass testpass --db-name armada_upgrade || FAIL=1
      else echo "  mysql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    sqlserver|mssql)
      echo "==> SQL Server (${SQLSERVER_IMAGE})"
      port="$(port_for 2)"; name="armada_upgrade_ss_$$"
      CONTAINERS+=("$name")
      docker run -d --name "$name" -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=${SA_PASSWORD}" -p "${port}:1433" "$SQLSERVER_IMAGE" >/dev/null
      if wait_ready "$name" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" -b; then
        run_suite "sqlserver" --db-type sqlserver --db-host 127.0.0.1 --db-port "$port" --db-user sa --db-pass "$SA_PASSWORD" --db-name armada_upgrade || FAIL=1
      else echo "  sqlserver: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    *) echo "Unknown provider: $provider" >&2; FAIL=1 ;;
  esac
done

echo ""
echo "===================== UPGRADE TEST SUMMARY ====================="
echo "  baseline: ${FROM_REF} (${SHORT}) -> current checkout ($(git -C "$REPO_ROOT" rev-parse --short HEAD))"
for line in "${SUMMARY[@]:-}"; do echo "  $line"; done
echo "================================================================"
if [ "$FAIL" -ne 0 ]; then echo "RESULT: UPGRADE TEST FAILED"; exit 1; fi
echo "RESULT: UPGRADE OK"
