# Phase 5 rework 3 handoff

Branch: armada/codex-1/msn_mvy2bq0d_Yk9bYqngjCH (base 7bfdfb67c).
Commits: d41c554bf (captain usage edit lifecycle) and the dashboard commit containing this file.
SDKs: net10.0 selected (10.0.401 installed). Node v24.21.0.

## Findings reconciliation

1. [Blocking] View Usage and the CaptainDetail usage surface were absent: RESOLVED.
   Captains rows carry an ActionMenu item labeled exactly `View Usage` (catalog-translated in all
   eight locales) that opens `/captains/{id}?section=usage`; CaptainDetail renders the
   `#captain-usage` section and scrolls to and focuses it. Available for every state.
   Tests: `src/pages/CaptainsUsage.test.tsx` (five states, keyboard-only path, exact label),
   `src/pages/CaptainDetailUsage.test.tsx` (focus and scope), mutation: removing the item fails 7
   cases; dropping `?section=usage` fails 6.
2. [Blocking] Omitted usage fields were not preserved on captain edit: RESOLVED in d41c554bf.
   `CaptainUpdateFieldPresence` marks the four usage fields, `EntityUpdateMerger.MergeCaptain`
   restores omitted ones, `CaptainUsageCaptureValidator` rejects invalid Custom commands with 400
   before storage (REST create/update and WebSocket update_captain), and a changed stored
   configuration calls `UsageCaptureService.Invalidate()` plus a scheduler rebuild.
   Test: `E2E.Usage.captain_usage_edit_lifecycle` over loopback HTTP; reverting the merger fails it
   with "unrelated name edit must preserve enabled capture was false"; disabling the validator fails
   it with "expected <BadRequest> but got <OK>". `captain_usage_edit_invalidates_capture` pins the
   invalidation wiring by source contract (in-flight cancellation itself is covered by the existing
   Services.UsageScheduler invalidation cases, not re-run here).
3. Earlier batch-latest and settings-API findings: remain resolved (E2E.Usage passes).

## AC5 coverage (dashboard)

- Client/types/hook: `api/client.ts` Usage section, `types/models.ts`, `lib/useUsage.ts`
  (one latest request per 100 visible ids, at most 5 account pages, then truncation notice),
  `lib/usage.ts` (75/90 thresholds, highest fresh applicable window), `api/usageClient.test.ts`
  (URL, X-Token, empty capture body, 429 mapping against a stubbed fetch).
- States: `UsageState` keeps loading, error+retry, empty, disabled, stale, estimated,
  unsupported, AuthMissing (named host, no credential paste) and Failed (last-good age) distinct.
- Captain detail: `CaptainUsageCard` (windows, reset time and countdown, shared captains,
  Capture now with disabled reasons, cooldown/offline/queued feedback, post-capture refetch,
  sparkline) and `UsageCaptureFields` (partial PUT of usage fields only, client validation,
  edits kept after a 400, PTY explained as unavailable).
- Token usage: Tokens (unchanged, default), Overview (`UsageOverTimeChart`, keyboard and table
  bucket selection, reset segments, gaps) with `UsageMissionDrilldown` (exact filters, Unknown
  cost, per-currency totals, coverage notice), Limits (`AccountLimitsTable`). URL holds all state;
  filters survive failed requests.
- Settings: `UsageCaptureSettingsPanel` (GlobalAdmin; full group PUT preserving retention values,
  range validation, success state, disable warning naming observed accounts; read-only text for
  others).
- WebSocketContext: `usage.snapshot` events coalesce (750 ms) into `usageRevision`, also bumped on
  reconnect; views refetch rather than appending points.
- i18n: 185 new keys in es, zh-Hans, zh-Hant, yue-Hant, ja, de, fr, it; `lib/usageCatalog.test.ts`
  gates usage components, table-driven strings and the page additions.
- Parity: TUI/mobile manifests regenerated; reviewed exceptions in `DASHBOARD_PARITY.md`;
  Tui.Parity export count 452.

## Executed checks

- `dotnet run --project src/Test.Automated -c Release --framework net10.0 --no-build -- --suites E2E.Captain,Models.Captain,Services.CaptainService,E2E.ApiContract,E2E.AuthorizationCoverage,E2E.WebSocket,Tui.Parity,Client.RouteSurface,E2E.Usage,Dashboard`: exit 0, 280 cases passed.
- `npm ci` (existing node_modules), `npm run build`: exit 0. `npm run test:run`: 230 files, 1572 tests passed.
- `python3 scripts/tui/generate-parity-manifest.py --check` and the mobile `--check`: passed.
- Visual/keyboard: real components rendered with fixture data into the app shell with the built
  `dist/assets/index-*.css`, headless Chrome over CDP at 1280/768/390 in light and dark, 7 surfaces
  (captain card, disabled card, captains usage cells, settings, chart, drill-down, limits): 42/42
  passed (no pane clipping outside horizontal scrollers, no page overflow, 14px root, no text below
  11px except shared DataTable sort/caret glyphs, every Tab stop has a visible focus ring). One
  defect found and fixed (checkbox row squeezed at 390). Screenshots kept in scratch, not committed.

## Not verified / limitations

- No live provider capture, no live Admiral dashboard session; visual checks use rendered component
  markup, not the running app.
- Armada.Client batch `captainIds` serialization (see DASHBOARD_PARITY.md) is not fixed.
- TUI and mobile usage screens are reviewed exceptions, not implemented.
- A PUT that sends only `UsageCaptureCommand` for a Custom captain is rejected (400) because the
  model drops a command deserialized without Custom mode; clients must send mode with command.
- Full Test.Automated/xUnit/NUnit/Test.Smoke runs, MySQL/PostgreSQL/SQL Server providers and the
  API baseline regeneration were not run (no route/tool surface change).
- Landing on main is not verified by this stage.
