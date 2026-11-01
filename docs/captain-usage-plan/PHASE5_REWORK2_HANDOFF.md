# Phase 5 rework 2 handoff

Status: BLOCKED. This commit delivers the explicitly requested batch-latest
prerequisite. It does not deliver AC5 or the View Usage dashboard implementation.
Branch: armada/codex-1/msn_mvy2bq0d_Yk9bYqngjCH.
Base: c30a47c2166e32cc24e4c18e475d9900000f1cdf.
The commit containing this handoff identifies this correction.

## Findings reconciliation

The complete previous handoff hoa_mvy2v7ol_4AQ5C6tfh29 was retrieved. Its two
findings match CODEX.md; it contains no additional nonblocking findings.

1. UsageSnapshotQuery scalar-only identity: resolved at the REST/service contract.
   Latest accepts captainIds as 1..100 distinct nonempty tenant-visible IDs.
   Every ID is authorized before parsing secondary filters. Shared accounts keep
   canonical identity and full membership; filtering precedes account pagination.
   Missing observations return no row, never zero. Scalar captainId cannot be
   combined with the batch. Other usage endpoints reject the batch rather than
   silently ignore it. HTTP regression and OpenAPI assertions are described below.
   Dashboard adoption and bounded hook remain outstanding.
2. Missing View Usage and CaptainDetail usage surface: unresolved. The English
   label, shared localization, stable selected-captain anchor, all-state
   availability, permissions and keyboard navigation remain mandatory.
3. Earlier GET/PUT global usage settings prerequisites: remain resolved. Both
   E2E.UsageSettingsApi cases passed again in the final run. This establishes the
   server group contract, not captain create/edit/save behavior or its UI.

## Additional blocking prerequisite: captain mutation lifecycle

A live HTTP diagnostic reproduced configuration loss outside the declared
Phase 5 dashboard components:

1. Use E2EServerFixture, E2ETenantUser and the fixture SQLite database. Seed a
   tenant/user-owned captain with UsageCaptureEnabled=true,
   UsageCaptureIntervalSeconds=120, UsageCaptureMode=Custom, and command JSON
   {"executable":"/opt/usage/read-quota","arguments":["--json"]}.
2. PUT /api/v1/captains/{id} with only {"name":"usage-edit-probe-renamed"}.
3. Assert HTTP 200, then GET the captain through the same authenticated client.
4. Assert enabled capture is preserved. Actual result: false. The diagnostic
   failed at "unrelated name edit must preserve enabled capture was false".

This was an executed temporary E2E.Usage case named
captain_usage_edit_prerequisite_probe, not an inference from source. The run
exited 1 with 21 passing cases and this one failure. The temporary case was
removed after recording the evidence; no failing test or weakened assertion is
committed. The permanent batch regression remains unchanged and passes.

Source confirms the cause: CaptainUpdateFieldPresence has no usage properties,
EntityUpdateMerger.MergeCaptain does not preserve omitted usage fields, and
CaptainRoutes PUT writes the deserialized model directly through
_database.Captains.UpdateAsync after model validation. That route also has no
usage-command validation or immediate capture invalidation/rebuild callback.
Those latter observations are source inspection; malformed-command persistence
and an in-flight old-configuration capture were not exercised in this run.

Required prerequisite correction: establish captain usage mutation semantics
for omitted versus explicit values, preserve unrelated edits, validate Custom
configuration before mutation, and connect saved configuration changes to the
capture generation/scheduler. Cover real HTTP create/edit/save/error transitions
and in-flight configuration changes. This spans the captain mutation/lifecycle
server contract, beyond the dashboard files and the specific batch contract
explicitly authorized by the round-2 finding. CODEX.md's Rules say "If you
discover a necessary out-of-scope change, report it in your result instead of
expanding scope on your own." No additional captain lifecycle changes are
included. The existing settings fix is preserved.

PTY is independently unavailable in the current ClaudeUsageProbe and
UsageProbeHostRunner: both explicitly report no verified adapter. This is a
known runtime limitation, not the reason for this blocked outcome. A future
form must explain it and must not imply that a global flag enables an adapter.

Exploratory frontend edits were removed before final verification. No dashboard,
locale, TUI or mobile source changes are included in this commit.

## Contract and limitations

Use GET /api/v1/usage/latest?captainIds=<comma-separated IDs>&pageSize=100.
Batches of 100 IDs are accepted, 101 are rejected. Page size still defaults to
25 and is capped at 100. If matching captains have multiple account contexts,
follow TotalPages; a batch is not a promise of a single account per captain.
Split visible captain pages larger than 100 into bounded batches. Account
pagination no longer selects unrelated accounts ahead of the requested IDs.

The service retains its existing tenant-wide latest-state storage scan; this
change bounds batch input and response pages, not internal query complexity.
There is no new route, schema migration, database-provider SQL, MCP argument,
client method or permission exception. Live evidence uses fixture SQLite and
loopback HTTP, not a live provider. No model prompts or credential reads ran.

## Executed checks

No run_process tool was available. exec_command preserved process exit codes.
SDKs installed: 8.0.425 and 10.0.401; net10.0 is targeted and was selected.
Commands ran at repository root unless --prefix supplies the dashboard directory.

- /usr/local/share/dotnet/dotnet run --project src/Test.Automated -c Release --framework net10.0 -- --suites E2E.Usage,Client.RouteSurface --results usage-final-results.json
  Final restored source: exit 0, 25 passed, 0 failed/skipped.
  E2E.Usage.latest_batch_captains verifies filtering before paging with an
  unrelated account, canonical shared membership, no invented empty-captain row,
  accepted 100-ID input, rejected empty/duplicate/101-ID batches and scalar/batch
  combination, encoded commas, missing/foreign IDs, foreign visibility before
  a malformed secondary filter, unauthenticated rejection, tenant-user explicit
  foreign-tenant rejection, global-admin explicit-tenant behavior, rejection on
  other usage endpoints, and published OpenAPI location/bounds.
- Mutation: temporarily remove only the batch membership filter in LatestAsync;
  run /usr/local/share/dotnet/dotnet run --project src/Test.Automated -c Release --framework net10.0 -- --suites E2E.Usage --results usage-batch-mutation-results.json
  Runner exit 1, 20 passed/1 failed. New regression: expected TotalRecords 1,
  actual 2. Source restored in a Python finally block before final verification.
- Diagnostic prerequisite: same dotnet command with --results usage-edit-prerequisite-results.json
  and temporary case described above: exit 1, 21 passed/1 failed. Source restored.
- PATH=/Users/joelchristner/.nvm/versions/node/v24.21.0/bin:$PATH npm ci --prefix src/Armada.Dashboard
  Exit 0. Existing dependency audit reports 2 moderate and 2 high vulnerabilities;
  no package changes or audit repair was attempted.
- PATH=/Users/joelchristner/.nvm/versions/node/v24.21.0/bin:$PATH npm run build --prefix src/Armada.Dashboard
  Final unchanged dashboard: exit 0, TypeScript and Vite build passed.
- PATH=/Users/joelchristner/.nvm/versions/node/v24.21.0/bin:$PATH npm --prefix src/Armada.Dashboard run test:run -- src/pages/TokenUsage.test.tsx
  Exit 0, 1 file / 2 tests passed. This is limited existing token-chart evidence.
- PATH=/usr/local/share/dotnet:$PATH bash scripts/common/generate-api-surface.sh -f net10.0
  Exit 0: 518 REST routes, 264 MCP tools, 65 WebSocket commands, 77 events,
  97 CLI commands, 324 settings keys. No generated baseline file changed:
  optional REST query metadata is not represented in that frozen surface.
  The live OpenAPI assertion pins the new parameter separately.
- Python json.load parsed the Postman collection, API surface and locale catalog.
  This is JSON syntax checking, not a locale/missing-key gate or collection run.
- git diff --check passed before commit.

Full .NET and Vitest suites were not run because Armada runs post-stage checks.
Raw generated result files were removed after recording these observations.

## Acceptance and delivery remaining

- AC5 batch contract: server prerequisite resolved by the named live HTTP case.
  Visible-page hook/request-count and complete page hydration remain unverified.
- AC5 captain/global settings UI defaults, validation, persistence and negative
  transitions: outstanding. D3 global defaults remain covered server-side by the
  two UsageSettingsApi cases; captain mutation is blocked as described above.
- AC5 View Usage navigation, selected captain scope, keyboard operation and
  disabled/unsupported/no-data reachability: outstanding, not implemented.
- AC5 render states (loading/retry/empty/AuthMissing with host/Unsupported/
  Estimated/Failed with last-good age/Disabled/stale), fresh highest applicable
  window, 75/90 boundaries, post-capture/event/reconnect refresh: outstanding.
- AC5 Limits/history, URL filters, keyboard/table selection, exact mission
  drilldown, reset gaps, retained inputs after errors, coverage/unknown values
  and cost/currency presentation: outstanding. D5/D8 server behavior passes the
  existing usage suites; its UI remains unverified.
- Catalog/missing-key gates; 1280/768/390 light/dark visual, responsive and
  keyboard/accessibility evidence: not run. No implemented UI exists to verify.
- TUI/mobile screens/manifests and reviewed exceptions: outstanding. No review
  approval or parity completion is claimed.
- Independent Test Engineer and Judge AC5 verification, publication and actual
  main landing: pending. A branch push is not main landing. No main merge was
  requested or attempted by this Worker.

## File manifest

- src/Armada.Core/Models/UsageSnapshotQuery.cs: optional bounded captain-ID list.
- src/Armada.Server/Routes/UsageRoutes.cs: batch parsing, pre-validation scoping,
  and latest OpenAPI documentation.
- src/Armada.Server/UsageAccessService.cs: batch bounds/visibility checks,
  filtering before latest account pagination and rejection on other usage reads.
- src/Test.Shared/Suites/E2E/UsageSuite.cs: permanent live HTTP batch regression.
- docs/REST_API.md: exact batch bounds, scope, pagination and error semantics.
- Armada.postman_collection.json: latest request description and reusable example;
  existing scripts, authorization and variables are preserved. Not published.
- CAPTAIN_USAGE_PLAN.md: latest batch adoption and pagination UX requirements.
- CHANGELOG.md: marks the delivered prerequisite and incomplete Phase 5 scope.
- docs/captain-usage-plan/PHASE5_REWORK2_HANDOFF.md: reconciliation, evidence,
  scope boundary, remaining criteria and delivery status.

No files deleted. Necessary production scope extension is limited to the batch
contract explicitly named by finding 1. No Phase 6/7 implementation was added.
