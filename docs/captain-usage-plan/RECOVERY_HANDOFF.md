# Armada recovery handoff

Worker branch: armada/codex-1/msn_mvx8imbu_cOmK35XdZ2o.

## Landing gate

This is a pre-landing Worker handoff, not evidence of completion of the operator's full recovery goal.
Voyage vyg_mvx8imbf_NIzlyN9qwAg has LandingMode MergeAndPush. Test Engineer
msn_mvx8ipgs_I9xlQs7pxbt depends on this Worker; Judge msn_mvx8ipis_tPcWBikrxtF depends on that engineer.
Both were Pending during this work. Do not enqueue or land before their independent approval.
After landing, fetch origin and prove the reviewed commit is reachable from origin/main, then reconcile
historical records through supported tools. Do not turn rejected historical reviews into passes.

## Inventory and remote evidence

Armada enumerate returned all 125 vessel missions in one page and all four merge queue records in one
page. All four entries were already Landed; none targeted the outstanding branches. No competing
InProgress mission existed on this vessel besides this Worker. Fresh origin/main was
6dada0bbdd97d63fa2f8462406c67fb33e61a8aa. No source was edited before that fetch.

- AC2: fast-forwarded d035ee438e2fa345661462fa45f7777ae9af53c7, preserving the complete original
  6a87580a1 through 15e3eab35 history and every intervening rework/test commit once.
- Collections: merged the actual remote branch. fafb47a4a0bb2c1ccb6233e3ae9af1b78cef2dfa is the
  implementation; recorded 78371d8d104333be2975a2ea619afbcdb8146c4c is a later merge with main,
  not a competing implementation. Recovery merge 090e427c3 preserves both histories.
- Pagination: cancelled recovery 8b961ec27a23227fd8608788ae688d89db388217 has a tree identical to
  its main-side parent (git diff --exit-code 8b961ec27^2 8b961ec27 returned 0). The equivalent
  implementation 52e9c75c6 and reconciliation 8275a542f reached main via 21faee5c3; all three are
  ancestors of fetched origin/main. Its remote branch is no longer advertised, but the recorded local
  object is present. There is no unique diff to resurrect.
- Failed Judge: get_blackbox and Analysis for bbx_mvx7zq8k_jJ6zNBucfJa identify JudgeRejected /
  NEEDS_REVISION after remote cancellation failed. The evidence supports a product defect in that
  reviewed code, now addressed by d035ee438's request-specific cancellation. The generic restart
  recommendation was not applied because unchanged rejected code must not be restarted.

## Per-mission accounting

Final fetch observed origin/main advance to d035ee438e2fa345661462fa45f7777ae9af53c7.
Armada now records msn_mvx7xshf_uLAmFBPoY2q Complete (completed at 02:55:22 UTC).
Every AC2 hash below is now reachable from remote main; collection bounds and the new recovery fixes
remain unlanded. This external landing produced no new merge queue record (the same four historical
entries remain). It is reachability evidence, not proof of independent approval of this integrated
candidate. Historical review outcomes remain as recorded.

| Mission | Recorded commit | Accounting |
| --- | --- | --- |
| msn_mvx7xshf_uLAmFBPoY2q | d035ee438e | Already landed externally at d035ee438; Armada Complete; remote reachability verified |
| msn_mvx5uw3r_7iGyWvXpQt4 | 15e3eab350 | Rejected outcome retained; reviewed hash now reachable via later corrected AC2 landing; superseded |
| msn_mvx5uw3r_WGXgKmDptiK | 15e3eab350 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx5uw3r_XywwKYwd0d5 | ace82a6d23 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx4p47h_J8BAltgrL5D | fd04a13f03 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx4p47h_iVqAKbcfJXn | fd04a13f03 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx4p47h_2M2m9r3eHVn | dc4601fc6b | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx25urd_GEhkqtrmDE7 | fb819d1201 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx25urd_3NJtgW4d8ZS | fb819d1201 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvx25urd_ddkWcJ8nbbL | dd0275c671 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvwzyeo8_Y6TqZCymBdr | f1b976b88a | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvwzyemx_dNypNJyctPV | f1b976b88a | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvwzybwf_FXHI7L6qj3V | 6a87580a15 | Hash already reachable via d035ee438; historical outcome retained; reconciliation awaits integrated review |
| msn_mvw0z9ai_OmURrhHig8E | 78371d8d10 | Preserved in integrated branch; review and landing pending |
| msn_mvsmzs1f_ssuGLWjrsfT | 8b961ec27a | Already landed by equivalent recovery; do not restart |

## Additional defects corrected

- Harbor no-token launches inherited the parent process's ARMADA_MCP_TOKEN and ARMADA_MCP_URL.
  BaseAgentRuntime inherits ProcessStartInfo.Environment, while LocalHarborJobRunner only set these
  keys on its token-present path. The runner now explicitly clears both before applying the scoped
  request token/URL. The shim fixture supplies unrelated credentials deterministically, verifying both
  absence without a binding and replacement with a valid scoped binding. No shell-profile attribution.
- Alert result lists are capped at 1000 but complete TargetCount and RaisedCount now count every
  evaluated/raised item. Both periodic and on-demand logs use RaisedCount. The 1001-target test proves
  every alert persists and notifies, repeated evaluation deduplicates every item, result lists remain
  bounded, Truncated is true, and minimally deserialized count defaults are zero.
- Scorecards trimmed revisions before deriving compatibility components. An omitted historical bridge
  could split two retained compatible revisions. Pooling now precedes trimming, and revision omission
  sets Truncated. Returned ids remain the newest 500; old-only attempts retain the documented unmatched
  behavior. Measured capabilities still derive from all revisions before detail response trimming.
- Cleaned nullable/unused-field/XML-reference warnings in the recovered usage implementation to satisfy
  the zero-warning solution-build requirement. No warning suppressions or assertions removed.
- docs/ENUMERATION.md now documents the caps, newest retention, component behavior and complete counts.

## Acceptance evidence

| Requirement / decision | Named checks and assertions | Observed |
| --- | --- | --- |
| AC2 / D1 ownership and sharing | scheduler_two_captains_share_one_attempt_and_links; scheduler_tenants_with_shared_credentials_capture_separately; exact jobs/snapshots/member links, no cross-tenant attribution | PASS |
| AC2 / D3 defaults and scheduling | Services.UsageDefaults; scheduler_fake_clock_deterministic_jitter_and_minimum_floor; scheduler_groups_shortest_interval_and_command_fingerprints; omitted/null/full defaults, floor, deterministic time and jitter | PASS |
| AC2 lease and manual join | scheduler_restart_lease_blocks_then_expires; scheduler_lease_lookup_traverses_pages_of_newer_jobs; scheduler_gate_wait_consumes_the_lease; scheduler_manual_capture_respects_cooldown_and_server_switch | PASS |
| AC2 / D4 host execution | custom_probe_contract_round_trip_on_real_process; malformed/out-of-range/oversized-output cases; older_harbor_answers_unsupported_until_it_advertises_the_capability; no adapter implies Unsupported, no prompt/automatic PTY | PASS |
| AC2 request-specific remote cancellation | remote_probe_cancellation_terminates_only_that_harbor_process_tree; invalidated_remote_attempt_stops_the_harbor_process_before_its_permit_is_reused; real shell/child termination, unrelated probe survives, replacement capture succeeds | PASS |
| AC2 dynamic concurrency | scheduler_lowered_overall_limit_governs_later_attempts; scheduler_lowered_host_limit_governs_later_attempts; scheduler_raised_limits_admit_more_later_attempts; burst and cancelled-waiter cases | PASS |
| Prior Judge findings | production_harbor_supplies_the_usage_probe_helper; production_server_ticks_the_scheduler_while_capture_is_disabled; configuration-generation, disabled-captain, lease, timestamps, output bounds and dockless-host cases | PASS |
| Harbor token launch gap | Services.HarborMissionToken/harbor_launch_without_token_is_unchanged plus token-present and permission-policy cases; unrelated token/URL explicitly supplied to fixture | PASS |
| Collection bounds | E2E.ResponseShape; Services.ConfigCollectionBounds; Database.ModelProfiles/revisions_bounded_newest_kept and compatibility_marks_bounded; Services.ModelAlerts/action_history_bounded and evaluation_lists_bounded_counts_complete | PASS |
| Existing dashboard styling | recovery-visual-check.mjs, production CSS and representative existing markup; 30 surface/theme/width combinations, no clipped controls/page overflow, 14px base, accent focus rings and Running/Succeeded palette | PASS; not full authenticated flows |
| Tested pipeline and final landing | independent Test Engineer and Judge approval, MergeAndPush, remote-main ancestry, supported record reconciliation | PENDING; downstream stage prerequisite |

Regression challenge: restoring the three recovered production implementations while retaining the new
checks produced 33/36 passes and three failures (token leakage, missing complete counts, and missing
revision truncation). A separate scorecard-only revert produced 16/17 passes and the precise omitted
compatibility-bridge assertion failure. Restoring the fixes produced 36/36 passes. Temporary mutations
were restored and are not committed.

## Executed commands

Framework net10.0, SDK 10.0.401, newest installed targeted SDK (8.0.425 also installed).

- dotnet build src/Armada.sln -c Release: zero warnings, zero errors.
- dotnet run --project src/Test.Automated -c Release --framework net10.0 --no-build -- --suites
  Services.Usage,Services.Harbor,Services.Job,Services.Host,Services.Remote,Services.LocalHost,
  Services.ConfigCollectionBounds,Database.ModelProfiles,Services.ModelAlerts,Services.ModelScorecard,
  Services.ModelEvaluation,Services.WorkerEnvelope,E2E.ModelProfiles,E2E.ModelAlerts,
  Client.Contract.ModelProfile,E2E.ResponseShape,E2E.ApiContract,E2E.Authorization,Client.RouteSurface,
  Tui.Parity,Docs: 462 passed, zero failed/skipped, exit 0. Prefix list is one comma-separated argument.
- dotnet run --project src/Test.Automated -c Release --framework net10.0 -- --suites
  Database.ModelProfiles,Services.ModelAlerts,Services.HarborMissionToken: 36 passed, exit 0.
- ARMADA_TEST_SUITES=Services.Usage dotnet test src/Test.Nunit --framework net10.0 -c Release:
  66 passed, exit 0. Same filter on Test.Xunit: one aggregate passed, exit 0.
- With ARMADA_TEST_DB_TYPE=Postgresql|Mysql|SqlServer, HOST=127.0.0.1, PORT=55591|55592|55593,
  USER=postgres|root|sa and disposable fixture passwords, the no-build console command with suites
  Services.Usage,Database.ModelProfiles,Services.ModelAlerts passed 96/96 on EACH provider, exit 0.
  Images postgres:16, mysql:8.4, SQL Server 2022-CU27-GDR1; containers removed after completion.
- Dashboard: npm ci, npm run build, npm run test:run: build succeeded; 217 files / 1497 tests passed,
  exit 0. Node 24.21.0 required an explicit PATH entry in this mission shell.
- node docs/captain-usage-plan/recovery-visual-check.mjs: 30/30 passed, exit 0; detailed results in
  recovery-visual-results.json. Viewed the dark 390px settings screenshot. Chrome on macOS only.
- git diff --check: passed.

- dotnet test src/Test.Smoke --framework net10.0 -c Release: one aggregate passed, zero failed,
  exit 0 (2m57s). Final solution build repeated after the manual log correction: zero warnings/errors.

## Limitations and remaining delivery work

- macOS process fixtures only; Linux database containers do not establish Linux process-tree behavior.
  Windows/Linux process cleanup and the Avalonia Harbor application remain unverified here.
- No real provider credential or inference endpoint was exercised; provider adapters are phase 3.
  Phases 3-7 and missing phase-5 UI surfaces remain deferred. No public route/tool was introduced.
- Rendered checks cover representative markup using production CSS, not every authenticated page,
  chart interaction, or create/edit/save network flow. Existing Vitest behavior tests passed.
- No full Test.Automated, Test.Xunit or Test.Nunit run (only the stated targeted filters). Armada owns
  post-stage checks. API/parity baseline regeneration was not needed: route/tool surface unchanged.
- Manual log-count correction was included after the broad run; final build validates that trivial
  substitution. The complete-count behavior is exercised by the 1001-target regression.
- Final landing cannot occur before the dependent review stages. No old mission/voyage was falsely
  marked Complete, no failed review was rewritten, no queue was bypassed and no vessel setting changed.


## Final remote update

After pushing candidate b6ba4d307, git fetch origin observed main at d035ee438.
`git merge-base --is-ancestor origin/main HEAD` succeeds: the candidate already includes that exact
landing, so no duplicate merge or source adjustment is required. Every AC2 historical commit is an
ancestor of remote main; fafb47a4a / 78371d8d and b6ba4d307 are not. The correction in this report
changes accounting only and does not invalidate any executed product checks. Independent approval
and landing of the final integrated recovery branch are still required before remaining records can
be reconciled. The failed Judge was annotated through annotate_blackbox, not changed to a pass.
