#!/usr/bin/env sh
# Runs the Database test suite against SQLite, PostgreSQL, MySQL and SQL Server. WSL 2 counterpart of scripts/linux/run-db-parity-tests.sh.
# The server providers run in Docker containers, so Docker must work in this distro unless --providers is sqlite only.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh source-path=SCRIPTDIR
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL (https://learn.microsoft.com/dotnet/core/install/linux)."
PROVIDERS="sqlite,postgresql,mysql,sqlserver"
prev=""
for arg in "$@"; do
    [ "$prev" = --providers ] && PROVIDERS="$arg"
    prev="$arg"
done
case ",$PROVIDERS," in
    ,sqlite,) ;;
    *)
        wsl_require_cmd docker "Install Docker inside WSL or turn on Docker Desktop's WSL integration for this distro."
        docker info >/dev/null 2>&1 || wsl_die "Docker is installed but its engine is not reachable. Start Docker Desktop (with WSL integration for this distro) or the docker service."
        ;;
esac
wsl_run "$SCRIPT_DIR/../linux/run-db-parity-tests.sh" "$@"
