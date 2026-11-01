# Phase 5 rework 3 independent test review

Reviewed start commit 6257a693d on armada/codex-1/msn_mvy2bq0d_Yk9bYqngjCH.
Outcome: BLOCKED. Production code was not changed.

## Coverage Added

AC5 / Dashboard.Usage: useUsage.test.tsx adds two passing regressions:
- `reports an HTTP failure distinctly from empty success and retries the same batch`:
  failed read produces error, retry preserves exact request scope, empty success
  produces ready without inventing an observation.
- `ignores a late response from the previous nonempty captain page`: attach a
  deferred response before rendering, assert the known first request, switch
  captain pages, resolve the old request last, and verify only the new page remains.

Commands run from src/Armada.Dashboard with Node v24.21.0 on PATH:
- `npm ci`: exit 0; reported 4 dependency vulnerabilities (2 moderate, 2 high).
- `npx vitest run src/components/usage src/lib/useUsage.test.tsx src/lib/usage.test.ts src/lib/usageCatalog.test.ts src/pages/CaptainsUsage.test.tsx src/pages/CaptainDetailUsage.test.tsx src/pages/TokenUsage.test.tsx src/pages/TokenUsageTabs.test.tsx src/pages/Settings.test.tsx`:
  exit 0, 13 files / 73 tests passed.
- `npm run build`: exit 0 (tsc and Vite).
- `git diff --check`: exit 0 before this report was added.

Prior finding 1 (View Usage missing): resolved in exercised tests. CaptainsUsage
covers each supplied state, exact English label, keyboard navigation, batch reads,
and destination scope. CaptainDetailUsage verifies section focus for the selected
disabled captain with no data. usageCatalog verifies all shipped translations and
placeholder preservation. TokenUsage's two existing chart tests passed.

Prior finding 2 (rename disables capture): source inspection confirms the existing
E2E.Usage.captain_usage_edit_lifecycle test checks preservation after a name-only
PUT and rejection of invalid Custom commands. Backend execution was not repeated
before this frontend blocker; resolution is not independently established here.

## Negative Paths

Existing targeted tests pass for batch failure, unknown percentages, expired
windows, stale/failed observations, interval bounds, Custom shell syntax and
non-admin controls. New tests exercise failed-read retry and late completion.

Temporary regression `allows capture again after cooldown expires and an event
refreshes usage` was inserted into CaptainUsageCard.test.tsx, run, and removed
because this stage must not commit known-failing tests. Command:
`npx vitest run src/components/usage/CaptainUsageCard.test.tsx` returned exit 1,
10 passed / 1 failed. The failure was `expect(element).toBeEnabled()` with the
Capture now button still disabled and title `Cooling down until at(2026-11-01T11:00:00Z).`.

## Affected Case

File: src/Armada.Dashboard/src/components/usage/CaptainUsageCard.tsx:39
Related state: src/Armada.Dashboard/src/lib/useUsage.ts (capture feedback).

Requirement: AC5 manual capture, cooldown feedback and retry/post-event refresh
must permit an eligible administrator to capture after the cooldown ends.

Reproduction using the existing CaptainUsageCard test fixtures:
1. Resolve getUsageLatest with page([view('Ok')]); render enabled baseCaptain
   with canManage=true and wait for its progress bars.
2. Mock captureCaptainUsage rejection as new ApiError('cooldown', 429,
   { nextEligibleUtc: '2026-11-01T11:00:00Z' }); click Capture now.
3. Wait for the cooldown feedback, then spy Date.now to return
   Date.parse('2026-11-01T12:00:00Z').
4. Increment the mocked usage revision, rerender the same card and wait for a
   second getUsageLatest call to complete.
5. Assert Capture now is enabled. Actual: it remains disabled.

Expected: expired cooldown no longer blocks capture (including recovery when
nextEligibleUtc is absent). Actual: captureBlocked depends only on
feedback.kind === 'cooldown'; neither time passage nor successful latest reads
clear that state. The disabled button cannot invoke capture to change it.
Unmounting the card resets state, but is not an acceptable recovery requirement.

Required correction: make cooldown expiry drive button availability, ensure a
mounted card updates at expiry, and provide recovery for unknown next-eligible
times. Add a permanent regression for expiry and event refresh after the fix.

## Residual Risks

Review stopped on the reproducible production defect. No production fix or
failing test is committed. The full Vitest suite is deferred to Armada's
post-stage checks as instructed. Backend, real-provider capture, the complete
viewport/theme/keyboard matrix, and actual main landing were not independently
verified. No merge or landing was performed. Worker visual/backend claims remain
inherited evidence, not results of this review. The tentative empty-page request
invalidation concern was not exercised and is not asserted as another finding.

## Revalidation at start commit 4a564ca21

The existing cooldown finding remains unresolved in the current source.
Independent rerun on 2026-11-01:
- `npm ci`: exit 0; 4 audit vulnerabilities reported (2 moderate, 2 high).
- Temporary `recovers capture after cooldown expiry and event refresh` in
  CaptainUsageCard.test.tsx: capture rejects with 429 and nextEligibleUtc one
  minute ahead; Date.now advances to one minute after expiry; usage revision
  increments; the second latest read completes. Capture now remains disabled.
  `npx vitest run src/components/usage/CaptainUsageCard.test.tsx`: exit 1,
  10 passed / 1 failed at toBeEnabled. Original test file restored in finally.
- Restored targeted run: `npx vitest run
  src/components/usage/CaptainUsageCard.test.tsx
  src/pages/CaptainsUsage.test.tsx src/pages/CaptainDetailUsage.test.tsx
  src/pages/TokenUsage.test.tsx src/lib/useUsage.test.tsx
  src/lib/usageCatalog.test.ts`: exit 0, 6 files / 34 tests passed.

View Usage navigation and existing token-chart regressions remain green.
No production changes or known-failing tests were retained. Backend mutation
lifecycle, visual matrix, full suites and main landing were not rerun. The
required correction and blocked outcome above still apply.


## Revalidation at start commit c2743d0d9

- npm ci: exit 0; audit reports 2 moderate and 2 high vulnerabilities.
- Temporary test `CaptainUsageCard > recovers after expired cooldown`:
  reject capture with HTTP 429 and nextEligibleUtc 60 seconds ahead, advance
  Date.now by 120 seconds, increment usage revision and await the second latest
  request. Expected enabled Capture now; actual disabled with cooldown title.
  `npx vitest run src/components/usage/CaptainUsageCard.test.tsx`: exit 1,
  10 passed / 1 failed at toBeEnabled. Restored the original file in finally.
- An initial temporary fixture outside the describe block failed on inherited
  mock call counts; it was corrected into the describe block before the result
  above. This initial fixture failure is not product evidence.
- Restored command: `npx vitest run
  src/components/usage/CaptainUsageCard.test.tsx
  src/pages/CaptainsUsage.test.tsx src/pages/CaptainDetailUsage.test.tsx
  src/pages/TokenUsage.test.tsx src/lib/useUsage.test.tsx
  src/lib/usageCatalog.test.ts`: exit 0, 6 files / 34 tests passed.
- Prior View Usage finding: resolved by current navigation/focus regressions.
  Prior captain edit lifecycle finding: not independently rerun this turn.
  Prior cooldown finding: unresolved, reproduced above.
- No production changes or known-failing tests retained. No new permanent
  coverage added. Full suites deferred per stage instruction. Build, backend,
  real provider, visual/keyboard matrix and main landing not checked this turn.
  The empty-page in-flight concern remains untested and is not a finding.

Outcome remains BLOCKED under the Affected Case and required correction above.
