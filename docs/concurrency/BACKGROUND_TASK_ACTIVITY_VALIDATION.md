# Background task feedback and compact usage guidance

The Dashboard Jobs page/tab and its command shortcut are removed. `/jobs` and `Activity?source=jobs` bookmarks reach Events. Meaningful ongoing work remains in its owning workflow: imports open their batch, health work opens Vessel Health, and usage work opens Captain Usage. Internal job APIs and durable execution/lease state are retained. Routine UsageProbe jobs are excluded from the header's running-work count and announcements.

## Recorded outcomes and Needs You

JobService records `job.succeeded`, `job.failed` and `job.cancelled` events only after winning a terminal-state update, including stale-worker recovery and cancellation. Heartbeats do not create audit noise. Failed work also emits an Error signal. Expected capture discards (SettingsGenerationChanged, CaptureDisabled, NoMembers) remain audit-only. Event/signal payloads carry diagnostic job context and a related workflow URL, without copying opaque ResultJson. Audit-storage failures are logged without changing an already committed work outcome.

Needs You includes the latest actionable failed attempt per exact tenant, user, kind and name. A newer attempt supersedes the previous failure, including while queued/running. Successful work, operator cancellations and expected capture discards do not become interventions. Counts and paging are computed in the database using a shared query across providers, with deterministic time/ID ordering. This wave executes that query against SQLite. Ordinary users see their own jobs; tenant admins see their tenant; global admins retain their normal scope. Usage failure titles identify the runtime and their Open button preserves the host filter. The button is keyboard focusable. No new approval action or job endpoint is introduced; REST/MCP docs describe the additive `background_job_failed` kind.

Existing records are retained; historical job outcomes are not backfilled into new Events/Signals. Those surfaces continue to use their configured retention. Native task-status tools remain available; shared navigation no longer advertises the removed Dashboard Jobs shortcut.

## Captain Usage guidance

The three space-consuming account-scope, legacy-correlation and chart-instruction paragraphs now live under an About usage disclosure beside the filters. A compact Coverage badge marks applicable coverage guidance, and duplicate coverage messages are collapsed. Help is closed by default, opens by keyboard/touch and wraps in normal document flow. Chart keyboard instructions remain available to screen readers through aria-describedby. Request errors and actionable truncation notices remain visible. All supported locales include the new control/links.

## Verification

The full .NET 10 run exercised 6,810 descriptors: 6,795 executed passes, 14 conditional skips and one shared navigation catalog mismatch. Aligning the catalog exposed an obsolete Go Jobs expectation in the TUI rerun; 668 other TUI/core checks passed. The final corrected Router/job/inbox/database/API selection passes all 40 cases on both .NET 8 and .NET 10. No failing case remains unresolved. The final solution build succeeds for both frameworks with zero warnings/errors. New cases cover winning transitions, duplicate callbacks, private-result exclusion, stale workers, cancellation, benign capture discards, exact tenant/user scope, paged totals, retry supersession and runtime/host context.

All 1,858 Dashboard tests and 1,384 Mobile tests pass. Mobile/shared Dashboard strict lint, type checking, generated-route checks and both parity gates pass, with no planned manifest entries. Regenerating the public API surface leaves its baseline unchanged: 521 REST routes, 264 MCP tools, 65 WebSocket commands, 77 events, 97 CLI commands and 335 settings keys.

An owned disposable Admiral verified cancellation through the real REST API produces exactly one event. Real inbox reads include the failed capture, exclude routine/cancelled tasks, retain runtime context and link to the host-scoped usage view. Failure rows were seeded in the owned database for UI rendering; service tests independently verify failure-event/signal emission. See [API checks](background-task-api-results.json).

Events, Signals, Needs You and both legacy Jobs URLs were checked in light/dark themes at 1440, 768 and 390 pixels (30 cases). About usage was checked closed/open with real legacy token data at the same themes/widths (12 more cases). Checks require no document overflow, one shared breadcrumb, no Jobs tab, quiet routine probes, successful redirects, collapsed help by default, one copy of the coverage note and accessible chart descriptions. Screenshots were visually inspected for hierarchy, wrapping, controls and contrast. See [activity measurements](background-task-layout-results.json), [help measurements](usage-help-layout-results.json), [Events desktop](background-task-events-desktop.png), [Needs You phone](background-task-needs-you-mobile.png), [compact usage desktop](usage-help-collapsed-desktop.png) and [expanded help phone](usage-help-expanded-mobile.png). The owned server was stopped afterward; no user's running deployment was changed.

Manual checks: open Activity and confirm there is no Jobs tab; run a capture and inspect its outcome in Events. For a failed capture, check Signals and Needs You, then use Open to reach its host-filtered usage view. Retry and confirm the previous attempt is superseded. On Captain Usage, open About usage by keyboard or touch and confirm all three explanations remain available without occupying space when closed.

Release command: `./build-all.sh v1.3.0`; execution log: `/tmp/armada-v130-background-task-activity-build-all.log`.

## Follow-up: remove legacy attribution copy

Captain Usage no longer displays “Legacy records without captured membership are correlation only; account-specific views exclude them.” The account-wide scope note, chart instructions and other coverage warnings are preserved. The targeted Activity/chart/localization run passes all 23 tests, and the production build succeeds. Twelve light/dark desktop/tablet/phone checks confirm the legacy sentence is absent, the remaining two help paragraphs expand/collapse correctly, the page stays within the viewport and chart accessibility descriptions remain intact. The phone layout was visually inspected. The disposable server was stopped afterward.

Release command: `./build-all.sh v1.3.0`; execution log: `/tmp/armada-v130-remove-legacy-note-build-all.log`.
