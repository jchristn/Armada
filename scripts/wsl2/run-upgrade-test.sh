#!/usr/bin/env bash
# Upgrade test (V1 readiness W3.1): seed data on an older Armada (default: v0.9.0), then start the current build on
# the same database and verify everything survived the migration. The baseline source (git archive <ref> src) is
# built with scripts/common/upgrade-baseline-host in a temp dir; SQLite runs against a temp file, and PostgreSQL,
# MySQL and SQL Server each get a throwaway Docker container. The baseline never touches ~/.armada.
# Defaults: --from-ref is the v0.9.0 tag when it exists, otherwise the "release(v0.9.0)" commit; --providers sqlite.
# Set ARMADA_UPGRADE_DB_PORT_BASE to pin the container host ports (base+0..2). Exits non-zero if any provider fails.
# WSL 2 version: uses the Linux git and dotnet in this distro, and the docker CLI for the server providers.
#
# Usage: scripts/wsl2/run-upgrade-test.sh [--from-ref <git ref>] [--providers sqlite,postgresql,mysql,sqlserver|all]
#                       [--framework net10.0] [--keep]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "ERROR: $*" >&2; exit 1; }
usage() { awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "${BASH_SOURCE[0]}"; }

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
# The helper's own errors get the same "ERROR:" prefix as every other Armada script.
wsl_die() { die "$@"; }
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
case "${1:-}" in -h|--help) usage; exit 0 ;; esac
# The suite runner below checks every exit code itself and keeps going after a failed provider.
set +e

RELEASE_COMMIT="e456b0080e5a834d5469cd615d73b3d542b00431"
FROM_REF=""
PROVIDERS="sqlite"
FRAMEWORK="net10.0"
KEEP=0

# need_value <flag> <value...>: a flag that takes a value must have one.
need_value() { if [ "$#" -lt 2 ] || [ -z "$2" ]; then echo "Missing value after $1" >&2; exit 2; fi; }

while [ $# -gt 0 ]; do
  case "$1" in
    --from-ref) need_value "$@"; FROM_REF="$2"; shift 2 ;;
    --providers) need_value "$@"; PROVIDERS="$2"; shift 2 ;;
    --framework) need_value "$@"; FRAMEWORK="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ "$PROVIDERS" = "all" ]; then PROVIDERS="sqlite,postgresql,mysql,sqlserver"; fi

wsl_require_cmd git "Install git inside WSL (for example: sudo apt install git)."
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
if [ "$(echo "$PROVIDERS" | tr '[:upper:]' '[:lower:]' | tr -d ' ')" != "sqlite" ]; then

# Docker Desktop's credential helper (docker-credential-desktop.exe) lives in a Windows folder that
# wsl_sanitize_path dropped from PATH; put that one folder back (at the end), so docker login, push and pull keep
# working. Without it, a config that names the helper fails clearly here rather than halfway through a push.
docker_credential_dir() {
    local dir old_ifs="$IFS"
    IFS=:
    for dir in $WSL_ORIGINAL_PATH; do
        if [ -f "$dir/docker-credential-desktop.exe" ]; then
            IFS="$old_ifs"
            printf '%s\n' "$dir"
            return 0
        fi
    done
    IFS="$old_ifs"
    return 1
}
if CREDENTIAL_DIR="$(docker_credential_dir)"; then
    case ":$PATH:" in *":$CREDENTIAL_DIR:"*) ;; *) PATH="$PATH:$CREDENTIAL_DIR"; export PATH ;; esac
elif grep -Eq '"credsStore"[[:space:]]*:[[:space:]]*"desktop(\.exe)?"' "${DOCKER_CONFIG:-$HOME/.docker}/config.json" 2>/dev/null; then
    die "Docker is set to use Docker Desktop's credential helper (credsStore in ${DOCKER_CONFIG:-$HOME/.docker}/config.json), but docker-credential-desktop.exe is not on the Windows PATH. Turn on Docker Desktop's WSL integration for this distro, or remove the credsStore line."
fi
wsl_require_cmd docker "Install Docker inside WSL, or turn on Docker Desktop's WSL integration for this distro (Settings > Resources > WSL integration)."
docker info >/dev/null 2>&1 || die "docker is installed but its engine is not reachable. Start Docker Desktop (with WSL integration for this distro) or the docker service in this distro (sudo service docker start)."
fi

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
( cd "$WORK/baseline/src" && dotnet build UpgradeBaselineHost/UpgradeBaselineHost.csproj -c Release -f "$FRAMEWORK" -p:TargetFramework="$FRAMEWORK" -p:TargetFrameworks="$FRAMEWORK" -o "$WORK/baseline-bin" --nologo -v q ) \
  > "$WORK/baseline-build.log" 2>&1 || { echo "Baseline build failed:"; tail -40 "$WORK/baseline-build.log"; exit 1; }

echo "==> Building test runner (${FRAMEWORK})"
( cd "$REPO_ROOT" && dotnet build src/Test.Automated --framework "$FRAMEWORK" -p:TargetFramework="$FRAMEWORK" -p:TargetFrameworks="$FRAMEWORK" -c Debug --nologo -v q ) \
  > "$WORK/runner-build.log" 2>&1 || { echo "Test runner build failed:"; tail -40 "$WORK/runner-build.log"; exit 1; }

export ARMADA_UPGRADE_BASELINE_HOST="$WORK/baseline-bin/UpgradeBaselineHost.dll"
export ARMADA_UPGRADE_BASELINE_REF="${FROM_REF} (${SHORT})"
# Anything that falls back to the default data directory lands in the temp dir, never ~/.armada.
export ARMADA_DATA_DIR="$WORK/runner-home/.armada"

rand_port() { echo $(( (RANDOM % 26000) + 33000 )); }
port_for() {
  if [ -n "${ARMADA_UPGRADE_DB_PORT_BASE:-}" ]; then echo $(( ARMADA_UPGRADE_DB_PORT_BASE + $1 )); else rand_port; fi
}

# start_container <label> <docker run args...> -- starts a detached container and fails fast (non-zero, with
# docker's own error) when docker cannot start it, instead of waiting out the readiness probe.
start_container() {
  local label="$1"; shift
  local err
  if ! err="$(docker run -d "$@" 2>&1 >/dev/null)"; then
    echo "  ${label}: docker run failed: ${err}"
    SUMMARY+=("${label} :: CONTAINER FAILED TO START")
    return 1
  fi
  return 0
}

wait_ready() {
  local name="$1"; shift
  for _ in $(seq 1 90); do
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
      if ! start_container "postgresql" --name "$name" --shm-size=256m -e POSTGRES_PASSWORD=testpass -p "${port}:5432" "$POSTGRES_IMAGE"; then
        FAIL=1
      elif wait_ready "$name" pg_isready -U postgres -q; then
        sleep 2
        run_suite "postgresql" --db-type postgresql --db-host 127.0.0.1 --db-port "$port" --db-user postgres --db-pass testpass --db-name armada_upgrade || FAIL=1
      else echo "  postgresql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    mysql|mariadb)
      echo "==> MySQL (${MYSQL_IMAGE})"
      port="$(port_for 1)"; name="armada_upgrade_my_$$"
      CONTAINERS+=("$name")
      if ! start_container "mysql" --name "$name" -e MYSQL_ROOT_PASSWORD=testpass -p "${port}:3306" "$MYSQL_IMAGE" \
        --skip-name-resolve --innodb-flush-log-at-trx-commit=0 --sync-binlog=0; then
        FAIL=1
      elif wait_ready "$name" mysqladmin ping -uroot -ptestpass --silent; then
        sleep 3
        run_suite "mysql" --db-type mysql --db-host 127.0.0.1 --db-port "$port" --db-user root --db-pass testpass --db-name armada_upgrade || FAIL=1
      else echo "  mysql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    sqlserver|mssql)
      echo "==> SQL Server (${SQLSERVER_IMAGE})"
      port="$(port_for 2)"; name="armada_upgrade_ss_$$"
      CONTAINERS+=("$name")
      if ! start_container "sqlserver" --name "$name" -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=${SA_PASSWORD}" -p "${port}:1433" "$SQLSERVER_IMAGE"; then
        FAIL=1
      elif wait_ready "$name" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" -b; then
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
