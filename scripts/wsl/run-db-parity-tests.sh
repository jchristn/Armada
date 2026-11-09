#!/usr/bin/env sh
# Runs the Database test suite against SQLite, PostgreSQL, MySQL and SQL Server. WSL 1 counterpart of scripts/linux/run-db-parity-tests.sh.
# The server providers run in Docker containers, which WSL 1 cannot host; only --providers sqlite works here.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
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
        wsl_die "WSL 1 has no Docker engine (needed for $PROVIDERS); pass --providers sqlite to run SQLite only. Turn on Docker Desktop's WSL 2 integration and use scripts/wsl2, or run scripts\windows\run-db-parity-tests.bat from Windows."
        ;;
esac
wsl_run "$SCRIPT_DIR/../linux/run-db-parity-tests.sh" "$@"
