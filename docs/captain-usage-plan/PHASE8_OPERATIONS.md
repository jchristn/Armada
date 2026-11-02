# Phase 8: retained history and operational readiness

Migration 115 adds numeric sample weight, latest observation status and raw-pruned
provenance to hourly rollups on SQLite, PostgreSQL, MySQL and SQL Server. Earlier
migration 114 is now replay-safe on PostgreSQL and SQL Server as well. SQLite
uses the existing ADD COLUMN replay guard; MySQL uses the existing duplicate-DDL
handling. No existing token or account observation is rewritten.

Retention consumes at most 500 raw rows per transaction, preserving the existing
200-batch pass bound. It locks the selected batch on server providers, merges its
contributions into the account/provider/window/model UTC hour, deletes exactly
those selected IDs and their member links, and commits once. A crash rolls back
both sides. Subsequent retries consume only remaining rows, and late historical
observations merge without refreshing the hour's latest observation time. The raw
cutoff is floored to an hour; observations may remain for up to one extra hour.

Numeric mean weights count numeric percentages rather than all statuses. Failure
and missing-percentage samples contribute status/count coverage but never zero
utilization. Latest status is independent of status totals: an earlier failure
cannot erase a newer recovery, nor can an earlier late success erase a newer gap.
Instant samples do not establish continuous coverage seconds, so that field stays
null rather than inventing duration. Retained and remaining raw rows merge once
when a batch splits an hour; pre-existing non-pruned aggregates retain their
original overlap guard. Historical bounds crossing a display-bucket boundary
still produce the existing coverage warning.

The default rollup cutoff is 90 days. Latest state remains separate and retains
its original good/error observation ages. Closing the last member expires that
account's metadata; another active member prevents expiry. Closed membership and
expired metadata rows are pruned outside rollup retention. Tenant deletion now
removes all usage snapshots, links, memberships, latest state and rollups in the
same provider transaction. Token retention remains independent; deleted mission
or token history cannot be reconstructed from quota percentages.

Usage telemetry emits bounded operation/outcome counters, durations, active and
queued depth, observation age and retention deletion counts on the existing
Armada meter/source. Capture, provider/host, parser, storage, query, schedule and
retention paths are covered; safe W3C context travels with the host request.
Instrumentation exceptions are contained. A telemetry listener regression verifies
bounded label names and that a throwing sink cannot fail capture instrumentation.

The bundled compose stack uses a pinned collector, Prometheus, Tempo, Loki and
Grafana. It reuses the existing entry point and logging bridge, subscribes to
Watson spans, uses read-only configuration mounts, binds browser tools to loopback
and requires a configured Grafana password. Metrics/logs have one bundled export
path. The new Usage domain dashboard uses provisioned datasource UIDs; existing
bundled dashboards now use those same stable UIDs. The product home page links
administrators to the tools, with public URL overrides for proxy deployments.

An isolated disposable stack validated the collector configuration and accepted
synthetic OTLP metrics, traces and logs. A scrape returned the metric, Tempo
returned its trace, Loki query_range returned its log, and Grafana provisioned the
seven-panel Armada Usage dashboard plus Prometheus/Loki/Tempo datasources. These
checks use no production credentials or production data. They establish local
wiring, not deployment of the user's live Admiral.

## Verification

The full backend run discovered 6,614 cases: 6,583 passed, 14 environment/toolchain
cases skipped and 17 failed under the runner environment. Nine failures came from
MinIO rejecting writes at Docker's backing-disk free-space threshold. Its disposable
test data now uses a bounded 256 MiB tmpfs; all nine S3 contract cases pass on
recheck, without production storage changes or deleting unrelated Docker data.
Eight TUI failures came from NO_COLOR in the shell; the complete TUI suite passes
641/641 with NO_COLOR unset, as required by its Unicode/default-theme tests. These
rechecks resolve every failure from the broad run; no single clean broad rerun is
claimed. The later token-expiry/partial-hour regression is covered separately by
the final focused provider matrix.

The full database matrix passes: SQLite 653/653; PostgreSQL, MySQL and SQL Server
each 643 passing with 10 provider-specific skips. The final rollup/migration subset
passes 10/10 on each of all four providers, covering the final added expiry and
partial-hour checks. Focused feature suites pass 150/150; a later retention/API/
telemetry recheck passes 29/29. xUnit's selected-suite wrapper passes; NUnit passes
144 selected feature cases. Both .NET targets build with zero warnings/errors.

Dashboard production build and all 1,597 tests pass. Mobile TypeScript and all
1,383 tests pass. TUI parity passes with no planned entries; mobile manifest
consistency passes with its 23 unrelated pre-existing planned API items unchanged.
The API baseline is regenerated and both compose files validate. Sixty saved
Postman example assertions pass. The final UI pass also corrects the read-only
capture explanation and the warning when disabling inherited-on capture.

Live Gemini quota, successful PTY quota capture and the complete historical
viewport/theme matrix remain the earlier host evidence limitations documented in
the plan. They are not claimed by synthetic telemetry or fixture tests.

The native Chrome surface was unavailable during the final visual recheck
(cgWindowNotFound); no new visual-matrix completion is claimed.
