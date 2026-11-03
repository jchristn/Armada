# Outer capacity and stuck-admission observation validation

Global, runtime and endpoint observations extend the existing captain/vessel monitor. Runtime limits, occupied-slot compatibility accounting and normalized endpoint keys are shared with admission. Global execution occupancy is now a single SQL COUNT of Assigned/InProgress, used by both admission diagnostics and monitoring: it neither loads outputs/diffs nor double-counts a status transition across separate reads. All four database implementations compile; executed database tests in this wave use SQLite.

Deployment-wide observations have null tenant/user scope and are visible only to global administrators. They do not include contributing mission IDs, endpoint URLs or credentials in payloads. Per-alias endpoint limits remain the existing admission behavior: aliases share URL usage but can declare different limits. Overruns with mixed alias limits are explicitly unknown, rather than inferred to be bugs. No capacity observer preempts work.

Stuck-admission observation samples up to 64 Pending missions in rotating three-tick windows. A mission must remain Pending at the same version and pass readiness before and after three completed dispatch opportunities. Full preflight resolves scalar routing preferences on a non-persisted view, checks normal eligibility/capacity and launch policy/placement, and excludes missing launch handlers, in-flight work and known launch/provision failures. Memory/global scheduler deferrals do not count. Pending snapshots are reused by dispatch. Missing, failed and out-of-order observations reset or skip counters; reported episodes are deduplicated and deleted/assigned work is pruned. This is a sampled investigation signal, not proof of continuously available capacity. It neither retries nor cancels work. Large queues take multiple windows to cover.

## Regression results

Thirty new cases in Services.OuterGateInvariant and Services.AdmissionStuck cover slot-versus-captain accounting, strictest duplicate runtime limits, URL aliases and mixed limits, verification exclusion, unlimited-to-finite changes, artifact-free execution counts, privacy and bounded metric labels; consecutive opportunities, all reset conditions, overlapping windows, bounded queue coverage, full preflight, healthy real health-tick dispatch, and non-persisted voyage overrides. The existing monitor tests continue to pass.

The broad .NET 10 run passes 481 cases, including API contracts, admission/verification, routing, agent lifecycle, real HTTP/git/process concurrency and the 200-mission matrix. The .NET 8 run passes 414 service/database/routing cases. The Dashboard suite passes all 1,858 tests. Dashboard production build and the .NET 8/.NET 10 solution build pass with zero solution warnings/errors. The concurrency script includes both new suites; bash syntax validation passes.

## UX/layout/aesthetic and privacy validation

An owned local Admiral generated actual global/runtime/endpoint overrun events on its startup health tick from disabled/quarantined fixture assignments. A separately seeded stuck event supplied layout content; event generation/reset behavior is covered by the automated tests above. No model calls or response interception were used. Event list, endpoint event details, its linked endpoint editor, and stuck event details were checked in both themes at 1440, 768 and 390 pixels (24 cases). The endpoint link was actually clicked into the existing Configuration > Endpoints editor. Shared breadcrumbs, contained scrolling, message wrapping and mission links remain valid; no document horizontal overflow was measured.

The first dark-mode inspection found a fixed light payload background with inherited light text. EventDetail now uses the theme background/text variables. Final payload contrast is at least 13.35:1 across measured views. See [layout measurements](outer-monitor-layout-results.json), [desktop payload](outer-monitor-endpoint-desktop.png), [phone endpoint editor](outer-monitor-endpoint-editor-mobile.png), and [phone stuck event](outer-monitor-stuck-mobile.png).

Authenticated local REST checks confirm a regular scoped reader gets 404 for all three aggregate overrun events and 200 for its own mission's stuck event; the global administrator reads the aggregates. See [status-code evidence](outer-monitor-privacy-results.json). The test server was stopped after validation; no user Admiral or Harbor was modified.

## Manual validation

Lower a configured global/runtime/endpoint limit below current execution usage and wait for a health tick. Existing agents must continue. Inspect Activity > Events as a global administrator; known reductions should drain, while first/ambiguous observations remain unknown. Open an endpoint event and follow its entity link into the editor. Scoped readers must not see these aggregate observations.

Ordinary dependency, capacity, memory, Harbor-policy or placement waits must not produce admission-stuck observations. An unchanged Pending mission sampled ready before/after three dispatch opportunities produces one mission.admission_stuck event and one armada_admission_stuck_total increment. Follow the mission link and investigate scheduler claims/errors; the signal does not prove continuous readiness or trigger a retry. Runtime/endpoint/global observer behavior and these limitations are documented in REST_API, MCP_API and TELEMETRY.

General eligibility unification, legacy-field/migration cleanup, wider live-provider/load validation, hierarchy, wait telemetry and release integration remain open in the plan. Release command: ./build-all.sh v1.3.0; log: /tmp/armada-v130-outer-monitor-build-all.log.
