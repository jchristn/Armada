# Admission benchmark results

## Round 1 (2026-11-04)

Commit `857ddb210` (working tree clean), macOS 26.6.0, Arm64, 18 logical processors, .NET 10.0.12, SQLite. 200 missions per arm, 16 concurrent submissions, captains with 4 concurrent missions each, accounts at 10, 20, 30, 50 and 89 percent used, `MostRemainingAllowance`, reserve 10%, per-attempt reservation 0.25%. Reports: `baselines/admission-<candidates>.json`.

| Candidates | Mode | p50 ms | p95 ms | p99 ms | Admissions/s | Admitted | Accounting violations |
|---:|---|---:|---:|---:|---:|---:|---:|
| 10 | Enforce | 914 | 986 | 1145 | 0.75 | 200/200 | 0 |
| 10 | Observe | 954 | 1066 | 1189 | 0.7 | 200/200 | 0 |
| 10 | Disabled | 935 | 991 | 1082 | 0.73 | 200/200 | 0 |
| 100 | Enforce | 952 | 1082 | 1336 | 0.71 | 200/200 | 0 |
| 100 | Observe | 979 | 1161 | 1232 | 0.68 | 200/200 | 0 |
| 100 | Disabled | 986 | 1120 | 1299 | 0.67 | 200/200 | 0 |
| 1000 | Enforce | 1619 | 6084 | 15899 | 0.63 | 200/200 | 0 |
| 1000 | Observe | 1642 | 4869 | 8786 | 0.61 | 200/200 | 0 |
| 1000 | Disabled | 1106 | 1229 | 1330 | 0.65 | 200/200 | 0 |

### Findings

1. **Budget met at 10 and 100 candidates.** Observe and Enforce p95 admission stay within 20% of Disabled (about 1 s in every mode). Enforce concentrated work on the accounts with the most headroom and never published a reservation above an account's headroom.
2. **Budget missed at 1000 candidates.** Enforce p95 is 6.1 s and Observe 4.9 s against 1.2 s for Disabled. Server telemetry (`armada.selection.stage.duration`) from a diagnostic run attributes about 270 ms per admission to evidence reads and about 320 ms to the admission transaction. In isolation those take about 45 ms and 40 ms (policy evaluation is about 1 ms). The difference is contention: on SQLite every decision writes about 3,000 ledger rows (one candidate, window and reason row per captain) while dispatch holds its capacity lock and mission creation competes for the single writer. Multi-row INSERT batching was tried and was slower on SQLite (about 320 ms per decision), so it was not kept. Options for the owner: record a bounded candidate set per decision (the plan currently requires the complete comparison), move the ledger write out of the capacity lock, or run large fleets on a server database and benchmark there.
3. **Mission creation is the throughput limit in every mode.** `POST /api/v1/missions` took 23.7 s p50 and 26.0 s p95 under 16 concurrent clients (Disabled included) because creation dispatches inline; admissions per second stay near 0.7. This is outside captain selection and belongs to the concurrency plan's stress work.
4. **Harness note.** The Admiral's Prometheus exporter binds to `localhost` only, so the diagnostic scrape used `http://localhost:<port>/metrics` rather than `127.0.0.1`.
