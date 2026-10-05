# Performance Baseline

Armada's REST API was measured against a seeded install the size of a busy team (500 vessels, 10,000 missions, 1,000
voyages, 50 captains) and again at five times that. Everything the dashboard touches stays well inside the budgets
below. The one real problem was the header's background-activity indicator, which downloaded every job the install
had ever run on each poll. It now asks the server for active jobs only.

The numbers are a baseline for regressions, not a benchmark to compare against other products. They come from one
fast machine, and the absolute values matter less than how each endpoint moves when the data grows.

## How to reproduce

```bash
scripts/common/perf-baseline.sh                      # 1x dataset, 50 measured requests per endpoint
scripts/common/perf-baseline.sh --runs 20 -- --missions 50000 --voyages 5000 --vessels 2500 --jobs 20000
```

The script builds `Armada.Server` and `src/Armada.PerfSeed` in Release, seeds a throwaway data directory through the
database driver (so rows satisfy the real schema and foreign keys), starts the Admiral on `127.0.0.1:25060` with
`ARMADA_DATA_DIR` pointing at it, measures, stops the server, and deletes the data. Your `~/.armada` and the default
ports 7890 and 7891 are never touched. Pass `--keep` to keep the data directory and `--port` to move the server.

`scripts/common/perf-measure.py` sends 5 warmup requests and then 50 measured requests per endpoint over one
persistent connection, authenticated with a bearer credential the seeder creates for the default tenant's admin, and
reports p50, p95, max, and response size. It exits non-zero if any endpoint returns an error status.

The default dataset:

| Entity | Count | Shape |
|--------|------:|-------|
| Fleets | 10 | |
| Vessels | 500 | Spread across fleets; each has a vessel health row with mixed grades |
| Captains | 50 | All Idle |
| Voyages | 1,000 | 100 InProgress, the rest Complete, Failed, or Cancelled |
| Missions | 10,000 | 10 per voyage; in-flight voyages hold WorkProduced, PullRequestOpen, and LandingFailed missions |
| Jobs | 2,000 | All finished (the steady state of an install that imports and evaluates health regularly) |
| Ask threads | 300 | 3 tracked work items each |

No mission is Pending, Assigned, or InProgress, so the server started on this data never dispatches work or launches
an agent. Data expiry and the vessel health scheduler are off in the seeded settings so nothing changes underneath the
measurement.

## Machine

Apple M5 Max (18 cores), 128 GB RAM, macOS 26.6, .NET SDK 10.0.401 (net10.0, Release), SQLite. The machine was not
idle: other builds and test runs kept the load average between 8 and 17 during the runs, which is closer to a
developer laptop running captains than a quiet server.

## Budgets

| Class | Budget (p95) |
|-------|--------------|
| First page of any list (missions, voyages, vessels, captains, vessel health, Ask threads, jobs) | 300 ms |
| Deep page of a list | 500 ms |
| `GET /api/v1/status` and dashboard summary endpoints | 300 ms |
| Header activity poll (runs every 5 to 30 seconds per open dashboard) | 100 ms and under 10 KB |
| Full lists the dashboard loads with `pageSize=9999` | 500 ms |

Every endpoint is inside its budget at 1x and at 5x. The tightest at 5x are the missions deep page (74 ms p95) and the
status snapshot (50 ms p95).

## Results

All times in milliseconds, p50 / p95. "Before" and "after" are the 1x dataset; the 5x column is after the fix.

| Endpoint | Request | Before | After | Bytes (after) | 5x |
|----------|---------|-------:|------:|--------------:|---:|
| Status snapshot | `GET /api/v1/status` | 12.7 / 15.9 | 11.4 / 15.1 | 55,071 | 44.2 / 49.6 |
| Missions, first page | `GET /api/v1/missions?pageSize=25` | 2.0 / 2.7 | 1.9 / 2.4 | 17,652 | 5.9 / 6.8 |
| Missions, page 350 | `GET /api/v1/missions?pageSize=25&pageNumber=350` | 24.7 / 32.6 | 21.5 / 23.5 | 17,533 | 64.2 / 74.2 |
| Missions, status filter | `GET /api/v1/missions?pageSize=25&status=WorkProduced` | 1.1 / 1.5 | 0.7 / 1.0 | 15,999 | 1.0 / 1.5 |
| Mission summaries (dashboard home) | `GET /api/v1/missions/summaries?pageSize=10` | 1.6 / 2.1 | 1.3 / 1.5 | 6,219 | 5.3 / 6.2 |
| Voyages, first page | `GET /api/v1/voyages?pageSize=25` | 0.5 / 0.7 | 0.4 / 0.5 | 5,938 | 0.4 / 0.7 |
| Voyages, page 35 | `GET /api/v1/voyages?pageSize=25&pageNumber=35` | 0.5 / 0.9 | 0.5 / 0.9 | 5,929 | 0.4 / 1.0 |
| Vessels, first page | `GET /api/v1/vessels?pageSize=25` | 0.7 / 1.2 | 0.5 / 1.0 | 22,632 | 0.4 / 0.5 |
| Vessels, page 18 | `GET /api/v1/vessels?pageSize=25&pageNumber=18` | 1.0 / 1.6 | 0.6 / 1.0 | 22,633 | 0.4 / 0.5 |
| Vessels, all (dashboard home) | `GET /api/v1/vessels?pageSize=9999` | 10.3 / 13.3 | 6.4 / 8.1 | 450,608 | 9.1 / 14.3 |
| Captains, all (dashboard home) | `GET /api/v1/captains?pageSize=9999` | 0.9 / 1.3 | 0.6 / 0.9 | 14,107 | 0.4 / 1.3 |
| Vessel health, first page | `POST /api/v1/vessel-health/enumerate` | 1.1 / 1.7 | 0.7 / 1.2 | 18,624 | 1.1 / 1.7 |
| Vessel health, page 18 | `POST /api/v1/vessel-health/enumerate` | 1.3 / 1.8 | 1.1 / 1.9 | 18,667 | 1.4 / 1.7 |
| Vessel health summary | `GET /api/v1/vessel-health/summary` | 0.5 / 0.7 | 0.5 / 0.6 | 165 | 1.1 / 1.3 |
| Jobs, full list (Jobs page) | `GET /api/v1/jobs` | 16.5 / 21.3 | 13.5 / 18.1 | 861,336 | 128.6 / 165.4 |
| **Jobs, active (header poll)** | before: `GET /api/v1/jobs` (the old server ignored the filter); after: `?status=Queued,Running` | 10.3 / 15.5 (861,336 bytes) | **0.7 / 1.0** | **102** | 10.6 / 25.5 |
| Ask threads, first page | `POST /api/v1/ask/threads/enumerate` | 1.0 / 19.2 | 0.9 / 1.3 | 8,781 | 0.7 / 1.2 |
| Fleet action runs count (dashboard home) | `POST /api/v1/fleet-action-runs/enumerate` | 0.3 / 0.4 | 0.3 / 0.3 | 104 | 0.2 / 0.3 |

Run-to-run noise on this machine is a millisecond or two at these sizes, so small differences between the before and
after columns for endpoints that did not change are load, not improvement.

## What changed

The background-activity indicator polled `GET /api/v1/jobs`, which returned every job in scope, every 30 seconds
while idle and every 5 seconds while something ran, from every open dashboard tab. Jobs are never pruned (retention is
tracked as W3.4), so the poll grew without bound: 861 KB per poll at 2,000 jobs and 8.6 MB taking 129 ms at 20,000.
`GET /api/v1/jobs` now accepts `status`, `kind`, `pageNumber`, and `pageSize` and returns one page when any of them is
present, backed by a paged, filtered query on all four database providers. The indicator asks for
`status=Queued,Running`, which is 102 bytes when nothing runs. The unparameterized call is unchanged, so existing
clients keep working; the Jobs page still uses it.

The same full-table read happened server side. `JobService.MaintainAsync` (every health-loop pass) and the vessel
health service's orphan and last-scheduled lookups loaded the whole jobs table to find a few rows. They now query only
the status or kind they need.

## Not egregious yet, worth doing

These stay within budget at 5x but grow linearly, and they are the first places to look when a larger install feels
slow.

- **Missions ordered by creation time have no supporting index.** `missions` has many status and tenant indexes but
  none on `created_utc`, so every list sorts the matching rows and deep pages scan past the offset (74 ms p95 at
  50,000 missions). An index on `(tenant_id, created_utc)` and one on `created_utc` would fix both. The `jobs` table
  has the same gap on SQLite, MySQL, and SQL Server (only PostgreSQL has a status index), which is why the active-jobs
  poll still costs 10 ms at 20,000 jobs. Both need a schema migration on all four providers; they were left out of this
  change so as not to collide with the migration work in W3.
- **`GET /api/v1/status` is N+1 over active voyages.** For each Open or InProgress voyage it reads the voyage again
  and then its mission summaries, and it returns all of them: 276 KB and 44 ms with 500 active voyages. It is also not
  tenant-scoped. A single grouped query, and a cap on the voyages it embeds, would flatten it.
- **The dashboard home loads every vessel, captain, and fleet** (`pageSize=9999`) to compute counts and names. That is
  450 KB at 500 vessels and 900 KB at 2,500. Count endpoints, or the summaries the page actually shows, would remove
  most of it.
- **Ask thread enumeration decorates each thread with a query** for its tracked work (an N+1 bounded by the page size
  of 25). Cheap today; one grouped count would remove it.
- **The MCP `enumerate` tool's `jobs` entity pages in memory** after loading every job, unscoped by tenant. It should use
  the new paged query and the caller's scope (noted for the W1 security review).

## Not measured here

W4.5 also asks for vessel health evaluation throughput and Ask turn latency. Both are dominated by external work (git
and dependency tooling against real repositories for health, an agent CLI or model endpoint for Ask) rather than by
Armada's own code paths, so a seeded database cannot measure them meaningfully. They need a run against real
repositories and a real model, recorded with the hardware and model used.
