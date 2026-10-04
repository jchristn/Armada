#!/usr/bin/env bash
set -euo pipefail

BACKEND="${1:-all}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

print_header() {
cat <<'TXT'
Armada v0.9.0 is a reliability release. It adds:
- Dock lifecycle state and a renewable lease (docks.state / lease_expires_utc / owner_token).
- Process-liveness telemetry separate from the output heartbeat (captains.last_process_alive_utc).
- A review-stage deadline for the review-timeout watchdog (missions.review_deadline_utc).
- Bounded merge-queue retries and stuck-entry recovery (merge_entries.retry_count / lease_expires_utc).
- A durable coordination-lease table for multi-instance-safe mutual exclusion (coordination_leases).

Before applying any manual SQL:
- Back up the database.
- Confirm the target Armada instance is stopped or otherwise isolated.
- Verify whether automatic startup migration is acceptable instead.

Notes:
- Armada.Server applies these schema changes automatically on first startup after upgrade.
- Use this script when you need a controlled DBA-managed pre-stage or reviewable SQL handoff.
- All backends advance to schema version 44.

TXT
}

print_file() {
    local label="$1"
    local filename="$2"
    echo "-- ${label}"
    cat "${SCRIPT_DIR}/${filename}"
    echo
}

print_header

case "${BACKEND}" in
    sqlite)     print_file "SQLite" "migrate_v0.8.0_to_v0.9.0_sqlite.sql" ;;
    postgresql) print_file "PostgreSQL" "migrate_v0.8.0_to_v0.9.0_postgresql.sql" ;;
    mysql)      print_file "MySQL" "migrate_v0.8.0_to_v0.9.0_mysql.sql" ;;
    sqlserver)  print_file "SQL Server" "migrate_v0.8.0_to_v0.9.0_sqlserver.sql" ;;
    all)
        print_file "SQLite" "migrate_v0.8.0_to_v0.9.0_sqlite.sql"
        print_file "PostgreSQL" "migrate_v0.8.0_to_v0.9.0_postgresql.sql"
        print_file "MySQL" "migrate_v0.8.0_to_v0.9.0_mysql.sql"
        print_file "SQL Server" "migrate_v0.8.0_to_v0.9.0_sqlserver.sql"
        ;;
    *)
        echo "Usage: $0 [all|sqlite|postgresql|mysql|sqlserver]" >&2
        exit 1
        ;;
esac
