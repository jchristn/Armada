# Phase 4 salvage ledger and recovery RCA

Mission msn_mvxxnhfb_MVZJzose6td (voyage vyg_mvxxnhe7_fuMOcVSikFD), vessel Armada.
Base: fresh `origin/main` = `30358ef7824534abd6e7448cb2fc9d3c116355e1` (fetched at the start and
re-fetched before the inventory; unchanged).

## 1. Salvage inventory

Method: every page of `enumerate` for missions (vessel `vsl_muyl13zo_CIYwKTLiYWN`, 128 records,
statuses Complete 115, WorkProduced 8, Cancelled 1, Failed 1, InProgress 1, Pending 2), voyages
(78 records, all pages) and merge entries (4, all `Landed`). For each of the 94 mission branches
and each recorded `CommitHash`: `git merge-base --is-ancestor <commit> origin/main` (ancestry),
`git cherry origin/main <branch>` (patch equivalence), and for commits without a branch a
same-subject search plus `git range-diff` / `git apply --check` against an `origin/main` index.
Landing was never inferred from mission status.

| Mission / branch | Commits not ancestors of main | Disposition | Evidence |
|---|---|---|---|
| msn_mvxf3n23_V1oCE5oqulg (Phase 4 Worker chain, voyage vyg_mvxf3n14_yc8nA19kJNb) / `armada/codex-1/msn_mvxf3n23_V1oCE5oqulg` | 9: e2c86026d, 83254f26d, d954b67ac, 111580ee4, 109d354f3, 0c61915c4, 6a6b043c5, 5a0c4f8f8, a5da8794f | **Genuinely missing. Recovered** on this branch by fast-forward (the branch's merge base is exactly `30358ef78`, so no cherry-pick and no duplicated ancestry). The stdio blocker is fixed on top (section 2). | `git cherry` printed `+` for all 9; `git merge --ff-only` |
| msn_mvw0z9ai_OmURrhHig8E (configuration collection bounds) / `armada/claude-code-1/msn_mvw0z9ai_OmURrhHig8E` | 78371d8d1 (integration merge only) | **Already landed.** Its work commit `fafb47a4a` is an ancestor of main (it came through `090e427c3` "Recover unlanded model collection bounds alongside preserved AC2 work"); the branch tip is an "Integrate current landing target main" merge with no unique patch. Worktree clean. Nothing to recover. | `git cherry` empty; `merge-base --is-ancestor fafb47a4a origin/main` true |
| msn_mvsk8fza_CCaP6EEnuHm (no branch left) | 69381d0ef "Retire every get-all collection API and page History on the server" | **Landed as a rewrite**: `52e9c75c6` has the same subject; `git range-diff` pairs them (`!`, rebase-level differences). | range-diff 1:1 pairing |
| msn_mvwm89hi_5544xbOpc35 / `armada/codex-1/msn_mvwm89hi_5544xbOpc35` | 5f3dbfdc7 "Add durable evaluation history and linked results pages" | **Superseded.** Its added pages (`Evaluations.tsx`, `EvaluationDetail.tsx`, `Evaluations.test.tsx`) exist on main from `c52ca7be8` and `d15f681d5` (later, broader evaluation pages). Neither forward nor reverse patch applies, because main carries the newer version. Not cherry-picked: it would regress them. | `git cat-file -e origin/main:<file>` for each added file |
| msn_mvwuq57g_* (deepseek) and msn_mvwuq7iz_* (glm) branches | several | **Landed by patch equivalence** (rebased). | `git cherry` shows no `+` lines |
| All other 87 branches | none | Landed (ancestors of main). | ancestry |

Prior recovery voyage vyg_mvx8imbf_NIzlyN9qwAg (Complete): all three missions' commits (`26cce7093`,
`b524da942`) are ancestors of main. Verified, not duplicated. Phase 2/3 anchors `6a87580a1`,
`dd0275c67`, `dc4601fc6`, `ace82a6d2`, `15e3eab35`, `1eeb9e585`, `f4e88920f` are ancestors of main.

Failed voyages: vyg_mvlnmtdv, vyg_mvsk8fyw, vyg_mvske55y, vyg_mvvj301r and vyg_mvwm87at have no
unlanded Armada commits (rows above). vyg_mvsqzakf belongs to another vessel and
vyg_mvwzybvf shows no missions to this caller, so both are out of scope.

Active work: only this voyage's own Test Engineer and Judge stages are Pending. No other mission
overlaps the files changed here.

## 2. Product defect: stdio `capture_captain_usage {}` (AC4)

Reproduced on the recovered Phase 4 tip (`a5da8794f`) with the built Helm process
(`ARMADA_DATA_DIR=<temp> dotnet Armada.Helm.dll mcp stdio`, initialize, `notifications/initialized`,
then `tools/call`):

    capture_captain_usage {} -> {"result":{"content":[...missing required property 'captainId'...],"isError":true}}

Root cause: `McpRequiredArgumentGuard.Install` accepted only `McpHttpServer`, and only
`ArmadaServer` called it. `armada mcp stdio` builds a separate Voltaic `McpServer`, which has the same
private `_Methods` table and `RegisterMethod`, and never had the guard installed. So the HTTP fix in
`6a6b043c5` could not reach stdio.

Fix: `McpRequiredArgumentGuard.Install(McpServer, ...)` overload sharing one wrapper with the HTTP
overload. `McpStdioCommand` installs it after `toolSet.Register` and before `RunAsync`, and writes to
stderr when the dispatcher cannot be captured. After the fix:

    capture_captain_usage {}   -> {"error":{"code":-32602,...,"data":{"tool":"capture_captain_usage","argument":"captainId"}},"id":2}
    capture_captain_usage {"captainId":"cpt_missing"} -> tool result (handler ran; Unavailable over stdio)

Regressions (`E2E.UsageMcpTransportParity`):
- `stdio_missing_argument_is_protocol_error_and_recovers` spawns the real `armada mcp stdio` and checks
  four missing forms ({}, absent, only `harborId`, mis-cased `CaptainId`) for `-32602` with no
  `result`/`isError` and `error.data`. It then checks recovery in the same session: a present
  `captainId` reaches the handler, `get_captain {}` keeps `isError`, and a wrong-type `captainId`
  keeps `isError`.
- `http_session_recovers_after_protocol_error`: one stateful HTTP session gets -32602, then a present
  `captainId` reaches the handler (NotFound), then `usage_status` succeeds.
- `guard_installs_on_both_transports`: both overloads capture the pinned dispatcher, guard
  exceptions, and a source contract that stdio installs between registration and `RunAsync`.
- Mutation proof: disabling the stdio install (`if (false && ...)`) fails the live stdio case with
  `stdio protocol error object ... "isError":true`. Restoring it passes.

## 3. Recovery RCA

Timeline (from the mission records and black boxes bbx_mvxgxd25_Y30AayEgmRm and bbx_mvxgxnwx_qjXBAunlUWu):

1. Rounds 1-2: the Test Engineers blocked on AC4. Round 2's affected case cited the HTTP E2E test
   (`UsageTenantIsolationSuite.cs:61`). Rework Workers 1 and 2 produced evidence-only commits
   (`d954b67ac`, `109d354f3`: "Document ... blocker") after concluding that Voltaic had no hook.
2. Round 3 (escalated, `6a6b043c5`) fixed the HTTP path exactly as cited. The round-3 Test Engineer
   then reproduced the same requirement over stdio (`McpStdioCommand.cs:88/90`) and blocked again.
   The budget was spent (rounds 3, escalated), so `HandleCompletionCoreAsync` (HF-25 branch) failed the
   mission with `JudgeRejected` and the remediation "correct the product code ..., then restart the
   pipeline from the Worker". `VoyageSettlement.ApplyAsync` then failed the voyage because 1 of 9
   missions had failed.
3. The black box rule BBX-REV-002 offered one recovery path, `bbx-rev-002.restart` =
   `restart_mission msn_mvxg8z3z` (the **Test Engineer**). It ran at 09:04:45Z. `RestartAsync` reset
   that verifier to Pending on the unchanged branch. The Test Engineer may not change production code
   (its own output says "Production changes are prohibited for this Test Engineer stage"), so it
   could only add evidence (`a5da8794f` "docs: reconfirm Phase 4 stdio protocol blocker") and blocked
   again at 09:06:40Z.

Classification:

| Factor | Finding | Defect? |
|---|---|---|
| Product validation | stdio server never had the guard (section 2). | **Yes, fixed.** |
| Mission brief / affected-case scoping | The round-2 affected case named the HTTP test only. The round-3 Worker fixed what was cited, and stdio surfaced only at the last round. | Brief or verifier precision, not code. Recommendation: name every transport an MCP acceptance criterion covers. |
| Pipeline orchestration | `HandleCompletionCoreAsync` correctly refused automatic rework past the budget, and `VoyageSettlement.ApplyAsync` correctly failed the voyage on a failed mission. | No defect proven at either site. |
| Restart stage selection | The remediation said "restart from the Worker", but no supported operation did that. `restart_mission` on the failed verifier only re-ran verification, and the Worker (WorkProduced) cannot be restarted. The recommended recovery could never correct the product code. | **Yes, fixed.** |
| Landing / record reconciliation | Nothing landed. Records (Failed TE, Cancelled Judge, Failed voyage) are consistent with git. | No defect. |
| Repeated evidence-only attempts | Rounds 1-2 committed documentation instead of a fix. Stagnation detection keys on an identical commit hash or an identical affected-case key, and both changed. | Observation only (follow-up below). No code change here. |

Fix (`MissionService.Review.cs` `RestartAsync`, `MissionService.Rework.cs` `TryScheduleReworkAsync`):
a restart of a verifier stage that is `Failed` with `FailureKind` `JudgeRejected`, whose output has a
standalone BLOCKED line, and that sits in a voyage with a dependency, now writes the same rework chain
automatic rework writes. That chain is a Worker rework round on the preserved branch, a fresh verifier
and the re-pointed later stages, all in one transaction. The restart is operator-approved, so the
automatic budget and automatic escalation are skipped and normal routing picks the captain. The
restart description becomes "Operator restart guidance" in the Worker brief. The blocked verifier
keeps its affected case and becomes WorkProduced, as it would with automatic rework. Stages cancelled
after it are restored, the voyage is reopened, and the new Worker is returned and dispatched.
Restarts for any other failure (crash, timeout, unblocked output) behave as before. The HF-25 failure
text now names this restart, so the remediation can actually be executed.

Regressions (`Services.MultiRuntimeFlow`):
- `restart_blocked_verifier_sends_work_back_to_worker` reproduces the recorded shape (escalated round,
  budget spent, Judge cancelled, voyage Failed) and asserts that a Worker rework is returned (round 2,
  not escalated, with the original task and the operator guidance). It also asserts that the verifier
  is WorkProduced with its affected case, that a fresh Test Engineer exists and the Judge follows it
  as Pending, that the voyage is InProgress, that no failed verifier remains, and that a second
  restart is refused.
- `restart_unblocked_failed_verifier_restarts_itself` (negative): a crashed verifier, with or without
  a BLOCKED line, restarts itself on the Worker branch and no rework is written.
- Mutation proof: bypassing the new branch fails the positive case with
  `expected <Worker> but got <Test Engineer>`, which is the recorded dead end.

## 4. Checks run (Release, net10.0; SDK 10.0.401 is the newest installed)

- `dotnet build src/Test.Automated -c Release -f net10.0`: succeeded.
- `ARMADA_TEST_SUITES=E2E.UsageMcpTransportParity,E2E.UsageTenantIsolation`: 9/9.
- `ARMADA_TEST_SUITES=Services.MultiRuntimeFlow,Services.PipelineDispatch`: 81/81.
- `ARMADA_TEST_SUITES=E2E.Usage,Client.Contract.Usage,Services.Usage,Client.RouteSurface,Client.Coverage,E2E.ApiContract,E2E.Authorization,E2E.McpTool,E2E.McpTenantIsolation,Tui.Parity`: 298/298.
- `ARMADA_TEST_SUITES=BlackBox,Services.BlackBox,Services.HandoffArtifact,Services.ContractRepair,Services.PromptSignal,...`: 559 passed, 2 skipped, 0 failed.
- Live stdio reproduction before and after the fix (section 2).

Not run here: the full suite and the smoke host (left to the pipeline's post-stage checks),
MySQL/PostgreSQL/SQL Server providers (no persistence change), the dashboard (no TypeScript change)
and Postman execution (no REST shape change). No API surface changed: there is no new route, tool,
argument or schema, so `docs/api-surface-1.0.json` needs no regeneration.

## 5. Follow-ups and operator recommendations (not code changes here)

- After this mission lands, **do not** run `restart_mission msn_mvxg8z3z_CppHN9bkIIJ` on an Admiral
  that has this fix: the fix is already on main, and the restart would open a duplicate rework chain on
  the old Phase 4 branch. Resolve bbx_mvxgxd25_Y30AayEgmRm and bbx_mvxgxnwx_qjXBAunlUWu (and the
  restart's entry bbx_mvxlvsu9_9gS7xuAjQfV) with the landed main SHA through `resolve_blackbox`.
- Brief authors: an MCP acceptance criterion should name every transport (HTTP session, HTTP
  stateless, stdio).
- Possible Armada follow-up: treat a rework round whose commit touches only documentation as stagnant
  work, so escalation happens earlier.
- A Judge (not a verifier) that fails at the budget still restarts itself with its prior findings.
  That is the existing re-evaluation design and was not changed.

## 6. Independent Judge verification (2026-11-01)

Reviewed tip `1c3357600c03d6d56a0d20c53e4e64bbad7c5757` against a fresh fetch of
`origin/main` (`30358ef7824534abd6e7448cb2fc9d3c116355e1`). No production source
was changed by the Judge.

- Independently enumerated all 128 vessel missions, all 78 voyages and all four
  merge entries (each fit one page at pageSize 200). Only this Judge is active.
- Checked every recorded mission commit against main and HEAD, and patch
  equivalence for surviving mission branch refs. All nine preserved Phase 4
  commits are ancestors of HEAD. Other non-main recorded commits match the
  ledger's rewritten, superseded or integration-only dispositions. Historical
  deleted branch refs cannot be re-enumerated from git; their recorded commits
  remain available for the ancestry audit.
- Independently confirmed the configuration-bounds work `fafb47a4a`, prior
  recovery commits `26cce7093`/`b524da942`, and every Phase 2/3 anchor listed above
  are ancestors of main. Inspected the get-all retirement range-diff pairing
  and the newer evaluation history/detail, battery pagination and toast-link
  implementations on main supporting the superseded disposition.
- Read both original black boxes through Armada MCP. Their recorded failed
  verifier restart recommendation agrees with the RCA; settlement is a
  consequence, not a demonstrated defect.
- Created a fresh detached validation worktree at the reviewed tip. SDK
  10.0.401; `dotnet restore src/Test.Automated/Test.Automated.csproj` succeeded
  before validation. `dotnet build src/Test.Automated -c Release -f net10.0
  --no-restore` succeeded with zero warnings/errors (exit 0).
- Ran the built Test.Automated host with `--suites
  E2E.Usage,Services.MultiRuntimeFlow,Services.Usage,Client.Contract.Usage,Client.RouteSurface,Client.Coverage,E2E.ApiContract,E2E.Authorization,E2E.McpTenantIsolation,Services.PipelineDispatch`:
  **231 passed, zero failed/skipped, exit 0**. Prefix selection includes the
  live stdio and HTTP transport recovery cases, usage aggregation/events and
  tenant-isolation cases, and restart refusal/no-voyage/repeated-verifier cases.
- Prior stdio AC4 and blocked-verifier restart findings are resolved by current
  source and independently executed checks. No acceptance criteria were changed
  or exceptions added by this recovery. Phase 2/3 targeted usage regressions pass.
- `git diff --check` reports CRLF as trailing whitespace in the existing-format
  Postman collection; with `core.whitespace=cr-at-eol` it passes. This is a
  formatting observation, not a contract defect.

No blocking defect found. Full suite/smoke host remain the pipeline's post-stage
checks. Other database providers, dashboard checks and Postman collection
execution were not run by the Judge. Approval is for pipeline landing, not proof
of landing: main still lacks this recovery chain. After normal landing, fetch
main again, prove ancestry or patch equivalence, and reconcile the historical
records/black boxes using supported Armada operations with that main SHA.
