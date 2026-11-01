# Phase 5 rework 1 handoff

Status: blocked on a Phase 4 batch-latest contract prerequisite. This is a
prerequisite correction, not a completed Phase 5 dashboard implementation.
Branch: armada/codex-1/msn_mvy2bq0d_Yk9bYqngjCH.
Starting commit: 375f084abee81b14119d1ce2148651eab1eaf856.
The commit containing this handoff identifies the delivered correction.

## Prior findings

1. StatusRoutes.cs settings response omits UsageCapture: resolved. GET and PUT
   now return the effective group. E2E.UsageSettingsApi.get_defaults exercises
   real HTTP GET and checks every disabled-by-default setting. Removing the
   response member caused both new cases to fail (runner exit 1).
2. SettingsUpdateRequest.cs cannot bind/apply UsageCapture: resolved. The typed
   optional group now binds, applies in place, persists, invalidates active work
   and awaits schedule rebuilding. E2E.UsageSettingsApi.put_persist_apply_and_errors
   checks the exact minimal Enabled=true request, subsequent GET, file reload,
   all numeric bounds, retained settings references, generation invalidation,
   null/omitted groups, unrelated edits, explicit disable, malformed JSON/types,
   and unauthenticated/tenant-admin rejection without mutation. Marking the
   property JsonIgnore reproduced failure at "minimal request binds Enabled"
   (runner exit 1). Both temporary mutations were restored.

The complete prior handoff hoa_mvy2f436_RyMzaJSiMnX was retrieved. It contains
these two findings, no additional nonblocking findings and no executed build,
Vitest, visual or keyboard checks. No inherited claims are counted as checks here.

## Settings contract

Server settings remain GlobalAdmin-only. Omitted or null UsageCapture preserves
the group; a supplied object replaces group values using model defaults for
omitted members, consistent with existing nested settings groups. Thus an empty
object disables capture by default. Numeric values use UsageCaptureSettings
clamps; invalid field types return HTTP 400 before any settings mutation.
The PUT OpenAPI description records all bounds and replacement semantics.
Existing objects stay live for UsageAccessService and passive header handlers.
After saving, the production callback invalidates the capture generation and
awaits UsageCaptureScheduler.RebuildAsync, including when disabling capture.
No token recording, provider probing or credentials were changed.

## Blocking prerequisite and scope

AC5 requires a bounded latest request for the visible captain page without
per-captain fanout. The landed Phase 4 API cannot express that request:

- Core Models/UsageSnapshotQuery.cs exposes only scalar CaptainId. UsageQuery
  inherits it without a captain-ID collection.
- Server Routes/UsageRoutes.cs ReadMetadata advertises scalar captainId;
  Parse reflects scalar query properties and has no batch input.
- Server UsageAccessService.cs LatestAsync filters members against one
  query.CaptainId, then paginates account groups. ScopeAsync caps pageSize at 100.
  There is no stable relation between a Captains table page and this account page.
- An unfiltered latest page can omit groups belonging to visible captains. A
  full tenant scan needs unbounded page requests as tenant size grows; individual
  captain requests create the prohibited fanout. Neither meets the AC5 contract.

Required prerequisite: a bounded tenant-authorized batch identity filter for
latest, with defined captain/page and shared-account behavior, visibility checks
for every requested captain, empty/missing/unsupported state semantics, and
regression/OpenAPI coverage. This changes the Phase 4 Core DTO, UsageRoutes and
UsageAccessService contract outside the declared Phase 5 dashboard components.
The two settings fixes were explicitly required by the verifier; this separate
contract extension was not. CODEX.md directs necessary out-of-scope changes to
be reported instead of silently expanding scope. No speculative client endpoint
or partial dashboard was added. Return to Phase 5 after that prerequisite lands.

## Acceptance and linked decisions

- AC5 settings prerequisite: the two named HTTP cases above pass. D3 global
  disabled defaults and invalid/minimal inputs are covered; existing captain
  defaults/mode behavior is covered by Services.UsageDefaults. The UI create,
  edit, validation and save transitions remain unimplemented/unverified.
- AC5 View Usage: CAPTAIN_USAGE_PLAN.md now explicitly requires the exact English
  ActionMenu label, localization, stable detail usage route/anchor, selected
  captain scope, keyboard navigation and availability in all non-data states.
  Implementation and navigation tests remain outstanding.
- AC5 batch latest: blocked as described above; no fanout workaround introduced.
- AC5 render states, freshness, 75/90 thresholds, capture/event/reconnect refresh,
  Limits/history, URL filters, keyboard/table bucket selection, exact drilldown,
  reset gaps, retained inputs after errors and unknown/coverage/cost displays:
  outstanding. D5 attribution and D8 cost UI assertions remain unverified.
  Existing backend usage/aggregation/isolation/event tests passed; they do not
  establish frontend behavior.
- Existing token charts were not modified. Their build/component regression
  checks were not run, so no frontend regression guarantee is claimed.
- Locale/missing-key gates, responsive 1280/768/390 light/dark visuals and keyboard
  accessibility checks were not run. No new shipped UI strings were added.
- TUI/mobile screens and parity manifest changes or reviewed exceptions remain
  outstanding. No exception is represented as reviewed.
- Independent Test Engineer and Judge verification of the completed AC5 scope
  remains outstanding. Main landing and publication are not verified; pushing
  this correction branch is not landing the full mission.

## Executed checks

No run_process tool was available; validation used exec_command and preserved
original process exit codes. SDK 10.0.401 was installed and net10.0 is targeted.

1. `/usr/local/share/dotnet/dotnet run --project src/Test.Automated -c Release --framework net10.0 -- --suites E2E.UsageSettingsApi --results usage-settings-results.json`
   Exit 0: 2 passed, 0 failed/skipped. This restored and built .NET dependencies.
2. `/usr/local/share/dotnet/dotnet run --project src/Test.Automated -c Release --framework net10.0 --no-build -- --suites E2E.RetentionSettingsApi,E2E.Usage,Services.UsageDefaults,Services.UsageScheduler --results usage-adjacent-results.json`
   Exit 0: 65 passed, 0 failed/skipped. Prefix E2E.Usage includes the settings,
   usage query, aggregation, events, tenant isolation and MCP transport cases.
3. Mutation checks used command 1 with `--results usage-mutation-results.json`,
   rebuilt separately for each temporary mutation and restored source in a
   Python finally block. Both runner exits were 1 with the failures above.
4. `/usr/local/share/dotnet/dotnet run --project src/Test.Automated -c Release --framework net10.0 -- --suites E2E.RetentionSettingsApi,E2E.Usage,Services.UsageDefaults,Services.UsageScheduler --results usage-final-results.json`
   Exit 0 after restoring mutations and improving the missing-group assertion:
   65 passed, 0 failed/skipped, 13348 ms.
5. `git diff --check`: exit 0 before final commit.

Full suites were deliberately not run because Armada performs post-stage checks.
Dashboard npm ci/build/test:run were not run: frontend implementation is blocked.
No upstream/provider integration was changed; live provider requests were not run.
The settings tests exercised a real private in-process Admiral over loopback HTTP
with its authenticated client, SQLite database and settings file, not fetch stubs.
They verify generation invalidation and an empty-group rebuild; cancellation of
live probes and nonempty scheduling are covered by existing scheduler cases,
not by a new settings-to-running-probe integration scenario.

## File manifest and necessary dependencies

- CAPTAIN_USAGE_PLAN.md: adds the explicitly requested View Usage UX contract.
- src/Armada.Server/SettingsUpdateRequest.cs: optional typed UsageCapture input.
- src/Armada.Server/Routes/StatusRoutes.cs: settings response, malformed-input
  handling, in-place application, persisted-change callback and OpenAPI semantics.
- src/Armada.Server/ArmadaServer.cs: necessary runtime wiring for the required
  settings fix; invalidates capture and rebuilds schedules after saved changes.
- src/Test.Shared/Suites/E2E/UsageSettingsApiSuite.cs: two independently discovered
  live HTTP regression cases for the two reported defects and adjacent paths.
- docs/captain-usage-plan/PHASE5_REWORK1_HANDOFF.md: findings reconciliation,
  evidence, scope boundary, precise remaining criteria and delivery limitations.

No other source files were added, modified or deleted by this rework.
