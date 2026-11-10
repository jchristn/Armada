#!/usr/bin/env bash
# Run the full Database test suite against every supported provider (SQLite, PostgreSQL, MySQL, SQL Server) to
# verify cross-provider parity. SQLite runs in-process. Each server provider runs against a throwaway Docker
# container on a random host port, torn down afterward. Exits non-zero if any provider has a failing test.
# WSL 2 version: uses the Linux dotnet in this distro and the docker CLI (Docker Desktop with WSL integration for
# this distro, or Docker installed in it). All WSL 2 distros share one network, so 127.0.0.1 reaches the containers.
#
# Usage: scripts/wsl2/run-db-parity-tests.sh [--providers sqlite,postgresql,mysql,sqlserver]
#                          [--framework net10.0] [--no-build] [--suites Database]
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

PROVIDERS="sqlite,postgresql,mysql,sqlserver"
FRAMEWORK="net10.0"
DO_BUILD=1
SUITES="Database"

# need_value <flag> <value...>: a flag that takes a value must have one.
need_value() { if [ "$#" -lt 2 ] || [ -z "$2" ]; then echo "Missing value after $1" >&2; exit 2; fi; }

while [ $# -gt 0 ]; do
  case "$1" in
    --providers) need_value "$@"; PROVIDERS="$2"; shift 2 ;;
    --framework) need_value "$@"; FRAMEWORK="$2"; shift 2 ;;
    --no-build) DO_BUILD=0; shift ;;
    --suites) need_value "$@"; SUITES="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

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
  ( cd "$REPO_ROOT" && dotnet run --project "$TEST_PROJECT" --framework "$FRAMEWORK" -p:TargetFramework="$FRAMEWORK" -p:TargetFrameworks="$FRAMEWORK" -c Debug ${DOTNET_NO_BUILD} -- \
      --suites "$SUITES" "$@" ) > "$out" 2>&1
  local code=$?
  local total
  total="$(grep -E '^Total:' "$out" | tail -1)"
  if [ -z "$total" ]; then
    echo "  ${label}: NO RESULT (see below)"; tail -20 "$out"
  else
    echo "  ${label}: ${total}"
    # Name the failing tests and their messages, so a failed run says what failed without the result files.
    if grep -q '^Failed Tests:' "$out"; then
      sed -n '/^Failed Tests:/,$p' "$out" | head -60 | sed 's/^/    /'
    fi
  fi
  SUMMARY+=("${label} :: ${total:-NO RESULT}")
  return $code
}

# start_container <label> <docker run args...> -- starts a detached container and fails fast (non-zero, with
# docker's own error) when docker cannot start it, so a missing image, port clash, or stopped docker daemon is
# reported at once instead of after the readiness wait.
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

# wait_ready <container> <probe command...> -- polls the probe for up to ~180 s, but gives up at once when the
# container is no longer running (for example it exited on a bad configuration).
wait_ready() {
  local name="$1"; shift
  for _ in $(seq 1 90); do
    if docker exec "$name" "$@" >/dev/null 2>&1; then return 0; fi
    if [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" != "true" ]; then
      echo "  ${name}: container stopped before it became ready; last log lines:"
      docker logs --tail 20 "$name" 2>&1 | sed 's/^/    /'
      return 1
    fi
    sleep 2
  done
  return 1
}

DOTNET_NO_BUILD=""
if [ "$DO_BUILD" -eq 1 ]; then
  echo "==> Building test project (${FRAMEWORK})"
  ( cd "$REPO_ROOT" && dotnet build "$TEST_PROJECT" --framework "$FRAMEWORK" -p:TargetFramework="$FRAMEWORK" -p:TargetFrameworks="$FRAMEWORK" -c Debug --nologo -v q ) || { echo "Build failed"; exit 1; }
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
      if ! start_container "postgresql" --name "$name" --shm-size=256m -e POSTGRES_PASSWORD=testpass -p "${port}:5432" "$POSTGRES_IMAGE"; then
        FAIL=1
      elif wait_ready "$name" pg_isready -U postgres -q; then
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
      if ! start_container "mysql" --name "$name" -e MYSQL_ROOT_PASSWORD=testpass -p "${port}:3306" "$MYSQL_IMAGE" \
        --skip-name-resolve --innodb-flush-log-at-trx-commit=0 --sync-binlog=0; then
        FAIL=1
      elif wait_ready "$name" mysqladmin ping -uroot -ptestpass --silent; then
        sleep 3
        run_suite "mysql" --db-type mysql --db-host 127.0.0.1 --db-port "$port" --db-user root --db-pass testpass --db-name armada_test || FAIL=1
      else echo "  mysql: container not ready"; FAIL=1; fi
      docker rm -f "$name" >/dev/null 2>&1 || true
      ;;
    sqlserver|mssql)
      echo "==> SQL Server (${SQLSERVER_IMAGE})"
      port="$(rand_port)"; name="armada_parity_ss_$$"
      CONTAINERS+=("$name")
      if ! start_container "sqlserver" --name "$name" -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=${SA_PASSWORD}" -p "${port}:1433" "$SQLSERVER_IMAGE"; then
        FAIL=1
      elif wait_ready "$name" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" -b; then
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
