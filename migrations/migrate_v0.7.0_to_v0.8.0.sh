#!/usr/bin/env bash
set -euo pipefail

BACKEND="${1:-all}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

print_header() {
cat <<'TXT'
Armada v0.8.0 adds normalized backlog/objective and refinement persistence.

Before applying any manual SQL:
- Back up the database.
- Confirm the target Armada instance is stopped or otherwise isolated.
- Verify whether automatic startup migration is acceptable instead.

Notes:
- Armada.Server applies these schema changes automatically on first startup after upgrade.
- Use this script when you need a controlled DBA-managed pre-stage or reviewable SQL handoff.
- SQLite advances to schema version 43; PostgreSQL, MySQL, and SQL Server advance to schema version 42.

TXT
}

print_file() {
    local label="$1"
    local filename="$2"
    echo "-- ${label}"
    cat "${SCRIPT_DIR}/${filename}"
}

print_header

case "$BACKEND" in
    sqlite)
        print_file "SQLite" "migrate_v0.7.0_to_v0.8.0_sqlite.sql"
        ;;
    postgresql|postgres|pgsql)
        print_file "PostgreSQL" "migrate_v0.7.0_to_v0.8.0_postgresql.sql"
        ;;
    sqlserver|mssql)
        print_file "SQL Server" "migrate_v0.7.0_to_v0.8.0_sqlserver.sql"
        ;;
    mysql)
        print_file "MySQL" "migrate_v0.7.0_to_v0.8.0_mysql.sql"
        ;;
    all)
        print_file "SQLite" "migrate_v0.7.0_to_v0.8.0_sqlite.sql"
        echo
        print_file "PostgreSQL" "migrate_v0.7.0_to_v0.8.0_postgresql.sql"
        echo
        print_file "SQL Server" "migrate_v0.7.0_to_v0.8.0_sqlserver.sql"
        echo
        print_file "MySQL" "migrate_v0.7.0_to_v0.8.0_mysql.sql"
        ;;
    *)
        echo "Usage: $0 [sqlite|postgresql|sqlserver|mysql|all]" >&2
        exit 1
        ;;
esac
