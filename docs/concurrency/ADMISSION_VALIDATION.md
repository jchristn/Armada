# Admission segment validation

The v1.3.0 scheduling segment separates vessel checks from shared capacity claims and balances equivalent captains by occupied slots. It changes no page markup or theme styles.

## Behavior

- .NET 10: 151 focused scheduling, routing, pipeline, API-contract and real-process end-to-end checks passed, including 200 missions across five vessels and ten captains.
- The full solution builds for .NET 8 and .NET 10 with zero warnings/errors.
- Per-vessel lock tests exercise independent vessels, timed-out waits, cancellation, idempotent disposal and cleanup after concurrent contention. Mixed-limit tests require forward progress and check captain, voyage and global bounds.

## UX/layout/aesthetic review

A disposable local Admiral served the actual production dashboard. Synthetic database assignments supplied three active mission links and a lowered captain limit (3 running / limit 2); no agent processes were launched. Captains, captain detail and a pending mission were rendered at 1440, 768 and 390 pixels. The first-run wizard was dismissed before capturing the pages.

Reviewed consistent global breadcrumbs, readable capacity/state labels, distinct active mission links, the waiting explanation and holding-mission links, action placement at the right of tables, wrapping at narrow widths, and existing button/card typography and spacing. All nine page/viewport combinations have no document-level horizontal overflow. Tables retain their own scrolling. See [measured results](admission-layout-results.json). Screenshots were reviewed locally and are not committed.

The synthetic over-limit assignments intentionally bypass admission to reproduce a previously lowered limit. These screenshots establish presentation of that state; scheduler behavior is established by the automated tests.

## Build checkpoint

After commit/push on `feature/v1.3.0`, run `./build-all.sh v1.3.0` to build/push/pull both architecture manifests for Admiral, dashboard and proxy, and build/test/package the VSIX. The command updates both `v1.3.0` and `latest`. Build logs are reported with the checkpoint result, separate from the UI evidence.
