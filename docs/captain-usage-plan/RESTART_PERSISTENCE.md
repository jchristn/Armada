# Account usage restart persistence — 2026-11-02

Latest account usage is a persisted projection, not scheduler state. The old
implementation nevertheless had three gaps: latest reads silently applied a
24-hour history window, capture intervals came from scheduler memory, and raw
observations and their latest projection were committed separately with projection
errors swallowed. Identity-less errors also relied on a remembered account key.

Migration 116 adds durable latest-account captain links, persisted requested and
effective intervals, and a safe capture-context fingerprint on every provider.
Capture commits the raw observation, snapshot member links, latest account state
and latest member links in one transaction. A restarted capture service can recover
a unique account context without inventing an identity or losing last-good data.
Upgrade reconstructs missing projections from retained identified observations,
preserving their timestamps and separate latest-good/latest-error summaries.

Latest reads have no implicit age cutoff. Explicit UTC bounds still apply, stale
observations keep their real age, and metadata survives raw snapshot retention.
The additive AccountId identifies the stable persisted account state; Id retains
its observation meaning. Backend sorting precedes paging and has stable account
ID tie-breaking. Existing SDK defaults remain compatible.

Account Limits now reuses the API Requests Filters card, labeled filter grid,
table wrapper and shared toolbar, with immediate dropdown filters, Reset, column
selection, refresh, persisted auto-refresh/page-size controls and query timing.
The six columns remain Captain ID, Host ID, Runtime, Limits, Updated, Interval.

Verification:

- Solution build, net8.0 and net10.0: zero warnings/errors.
- Database.UsagePersistence and Database.MigrationHygiene: 22/22 each on SQLite,
  PostgreSQL 17, MySQL 8.4 and SQL Server 2022. Includes database close/reopen,
  raw pruning, tenant-scoped durable links, atomic rollback on projection failure,
  v115 raw-only recovery, and repeated initialization.
- Services.Usage*, E2E.Usage*, Client.Contract.Usage and E2E.ApiContract: 141/141.
  A fresh capture service preserves a known account through an identity-less error.
- Final E2E.Usage* recheck after query timing adjustment: 24/24.
- Full dashboard suite: 1,598/1,598; final focused usage recheck: 17/17.
- Saved Postman Usage latest request against an isolated local server: four
  assertions pass (HTTP status, JSON, response schema, no credential/excerpt leaks).
- Native Chrome review at 1760x979, dark theme: compared the actual Account Limits
  and API Requests pages; inspected expanded/collapsed filters and the shared
  toolbar. An isolated seeded eight-day-old 42% weekly observation retained its
  captain, host, 300s effective interval and original age after terminating and
  restarting the server process, then refreshing the dashboard.

The local review used a disposable SQLite database and disabled capture; no live
provider call or production server restart was performed. Keep the configured
Armada database on persistent storage. No migration can recover an already-deleted
database or observations that expired before a missing projection was repaired.
