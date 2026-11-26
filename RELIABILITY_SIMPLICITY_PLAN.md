# Reliability and Simplicity Plan (v1.5.0)

Branch: `feature/v1.5.0`. Target release: Armada 1.5.0. Created 2026-11-25.

## Why this plan exists

Armada 1.4.0 can put an open model in a pipeline, but it cannot keep one there reliably. A five-part review of voyage
creation, plan parsing, dispatch, persona handoff, recovery, and landing (2026-11-25) found the same two patterns at
the root of most failures. Control state is kept as text: headings and HTML comments in mission descriptions, exact
marker lines in model output, and regular expressions over model names. And the fallback for almost any mistake is
the most expensive one available: run the whole stage again. A frontier model rarely trips over either. Qwen3.8 trips
over both several times per voyage, and every trip costs a full stage, a lost review, or a mission that needs a human.

The goal of 1.5.0 is concrete. When this plan is complete, an operator can build a pipeline that mixes Qwen3.8 with
Opus and GPT-6.1 in any persona, run it unattended, and trust the outcome: work lands, failures are classified and
recovered or escalated, nothing gets stuck or duplicated, and a weak model can never be the last word on whether work
passes. Section "Acceptance gate" turns that sentence into numbers.

The plan also pays down the complexity that made those failures hard to fix: two dispatch loops, two voyage dispatch
paths, three ways to create a voyage, seven failure taxonomies, six copies of the handoff state, and a state machine
that 65 call sites bypass. Simplification is not a side quest here. Most of the reliability work is impossible to do
once and test once until those duplicates are collapsed.

## How to use this document

Every actionable line is a checklist item with a stable ID (`RS-<phase>.<n>`). Update the box and append evidence as
you go; never delete an item. If an item turns out to be wrong, mark it `[~]` and say why.

| Mark | Meaning |
|---|---|
| `[ ]` | Not started |
| `[-]` | In progress (add your name or session and the date) |
| `[x]` | Complete. Evidence is required: commit hash, test suite ids, command and result, log path, or screenshot path |
| `[!]` | Blocked. Say on what, and who can unblock it |
| `[~]` | Dropped or superseded. Say why and what replaces it |

Tags on items:

- **[HUMAN]** only a person can do it (store consoles, two-factor sign-ins, approvals). An agent prepares everything
  around it and stops.
- **[DECISION]** needs Joel's answer before work that depends on it starts. Record the answer and date inline.
- **Surfaces:** which product surfaces the item touches. Every surface named there has a matching item in Phase 12,
  13, or 14; an item is not complete until those are too, or are explicitly marked not applicable with a reason.

Before starting an item, check "Prior decisions and archive constraints" for its id.

Each item lists "Start at" locations from the review. Line numbers are from commit `a2503b7a2` and will drift; search
for the quoted symbol if the line has moved.

Record build and test results in the [Validation log](#validation-log) at the end, not inside items, except for the
one-line evidence pointer.

## Ground rules

These apply to every item. A change that breaks one of them is not complete, however well it works.

1. **Requirements.** Follow `~/Code/Agents/requirements`, in particular `CODE_STYLE.md` (no `var`, no tuples, one
   class or enum per file, usings inside the namespace, XML docs on public members only, `<exception>` tags,
   `ConfigureAwait(false)` and a `CancellationToken` on every async method, configurable public members instead of
   constants, specific exception types), `BACKEND_ARCHITECTURE.md`, `BACKEND_TEST_ARCHITECTURE.md`,
   `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `I18N.md`, `TELEMETRY_REQUIREMENTS.md`,
   `AUTHENTICATION.md`, `BENCHMARKING.md`, `SIMULATED_USER_TESTING.md`, `REPOSITORY_REQUIREMENTS.md`,
   `INSTALLERS.md`, and `WRITING_DOCUMENTS.md`. Project rules in `CLAUDE.md` and `AGENTS.md` also apply.
2. **No em-dashes anywhere:** code, comments, XML docs, docs, Postman, changelog, commit messages, store copy.
3. **Persistence.** Every schema change is a numbered migration for SQLite, PostgreSQL, MySQL, and SQL Server, with
   relational columns or child rows (no JSON blobs for fixed contracts), registered in each provider, and covered by
   the database parity run and the upgrade test from the v1.4.0 baseline. The latest migration at plan time is 132 (131 and 132 shipped in 1.4.0); new work starts at 133.
4. **Tests.** New behavior gets Touchstone descriptors in `Test.Shared` so it runs under `Test.Automated`, xUnit, and
   NUnit. A bug fix gets a regression test that fails before the fix. Front-end changes get Vitest (dashboard), Jest
   (mobile), and TUI shape tests.
5. **API sync.** Any REST, MCP, or WebSocket change updates `docs/REST_API.md`, `docs/MCP_API.md`,
   `docs/WEBSOCKET_API.md`, `Armada.postman_collection.json`, the API surface file
   (`scripts/common/generate-api-surface.sh` -> `docs/api-surface-1.0.json`), and `Armada.Client` in the same commit.
6. **Compatibility.** No breaking REST or MCP changes in a minor release. New fields are additive with defaults that
   reproduce 1.4.0 behavior unless the item says otherwise and the CHANGELOG calls it out. A 1.5.0 Admiral must work
   with a 1.4.0 Harbor (degraded where needed, never broken), and the reverse must fail with a clear message.
7. **Versions.** Joel authorized 1.5.0 for this work in the request that created this plan (2026-11-25). Do not
   change any other version, and do not tag, sign, or publish to NuGet or Open VSX without a new instruction (see the
   runbook, section 16.4 note 10).
8. **Secrets and private data.** The repository is public. Never copy anything from
   `~/Documents/Employers/Lattice AI/Armada` into it: account emails, team ids, App Store Connect ids, key ids,
   reviewer credentials. Reference the runbook section instead. Private store details go in the git-ignored
   `src/Armada.Mobile/store-private.local.md`. Model endpoint hosts, the Conductor bearer token, and the Ollama Cloud
   API key go only in `benchmarks/config/gate-endpoints.local.json`. `.gitignore` ignores `*.local.json` and
   `*.local.md`; check with `git check-ignore -v <file>` before writing any secret, and scan every diff for them
   before committing (RS-14.8).
9. **Leave alone:** the untracked `assets/vscode/` folder. `AGENTS.md` changes only through RS-13.12.
10. **Commits.** Small, one item or a tight group per commit, message prefixed with the item id
    (`RS-5.2: classify unrecognized runtime errors as Unclassified`). Commit only when asked; push only when asked.

## Definitions

- **Reference models.** Frontier: Claude Opus on the Claude Code runtime and GPT-6.1 on the Codex runtime. Open:
  Qwen3.8 27B on the ApiEndpoint runtime, served by Ollama behind the maintainer's Conductor instance (Ollama
  `/api/chat` format, bearer token). "Other models" in the goal means the same results hold for DeepSeek 4.1, GLM,
  and inkling-2-mini, served the same way through Conductor, with Ollama Cloud as the fallback for DeepSeek, GLM,
  and Kimi when Conductor does not host them. A direct Ollama host on the maintainer's network is a last resort;
  prefer Conductor. Endpoint addresses, model path segments, tokens, and keys live only in the git-ignored
  `benchmarks/config/gate-endpoints.local.json` (shape in the committed `gate-endpoints.example.json`).
- **Reference pipelines.** The four pipelines in RS-1.6. The acceptance gate runs all four.
- **Human intervention.** Any action by a person after dispatch: retry, restart, manual land, edit, cancel, or
  config change. Approving a review gate that the pipeline is configured to require does not count.
- **Format-only failure.** The work was done but the report broke the contract (missing verdict line, malformed plan
  JSON, missing completion marker).

## Acceptance gate (definition of done for 1.5.0)

Phases 0 to 16 are the work. This gate is how we know the work achieved the goal. It is checked in RS-14.6 and must
pass on the release candidate commit. Joel set the thresholds on 2026-11-25 (RS-0.3): G1 at 99%, the rest as drafted.

| # | Measure (over the full gate run sized in [Run budget](#run-budget)) | Threshold |
|---|---|---|
| G1 | Voyages that reach a correct terminal state with no human intervention (pooled; per-pipeline counts reported) | at least 99% |
| G2 | Missions left in a non-terminal state with no actionable blocker for longer than 2x their stage timeout | 0 |
| G3 | Duplicate work: a stage executed twice for the same attempt, two rescues for one failure, or a mission landed twice | 0 |
| G4 | Format-only failures that caused a full stage rerun (instead of an in-session or format-only repair) | 0 |
| G5 | Format-only failures repaired without a full rerun | at least 90% |
| G6 | Runtime failures classified as `Unclassified` | under 2% of all runtime failures |
| G7 | Judge PASS on a seeded-defect task (`BatterySeededSetGrader`) | 0 |
| G8 | Voyages where an open model was the final Judge of its own tier's work without a tier rule exception | 0 |
| G9 | Crash consistency: Admiral killed at a random crash point (RS-1.3) in 50 runs; every voyage reaches the same terminal state as an uninterrupted run | 50 of 50 |
| G10 | Median time from mission Pending (ready) to agent process started, with free capacity | under 2 s (1.4.0: up to 10 s) |
| G11 | Database queries per dispatch pass with 50 pending missions (Armada.Benchmark admission runner) | at least 5x fewer than the 1.4.0 baseline |
| G12 | Escalations that went to a captain with a strictly higher effective tier or the configured ladder's next rung | 100% |

### Run budget

Joel set the model budget on 2026-11-25. Models hosted on the maintainer's Conductor instance (Qwen3.8,
inkling-2-mini, and anything else it serves) run as often as the work needs. DeepSeek, GLM, and Kimi run on Ollama
Cloud credits, so use them as little as a result allows. Opus and GPT-6.1 get few runs. The gate is sized to that:

| Run | Pipelines | Runs per battery pack |
|---|---|---|
| Development checks (any phase) | P-D and single-persona runs on Conductor models only | as needed |
| RS-0.8 baseline (1.4.0) | P-D: 10. P-A, P-B, P-C: 1 | |
| RS-14.6 gate | P-D: 10, plus P-D with inkling-2-mini in place of Qwen3.8: 10. P-A, P-B, P-C: 3 | |
| RS-14.6 additional families | DeepSeek 4.1, GLM, Kimi substituted on P-D only: 1 run per pack | |

G1 is computed over the whole gate run (about 260 voyages with eight packs). Frontier pipelines with 3 runs per pack
cannot show 99% on their own, so the report gives the per-pipeline counts next to the pooled figure; any failure on
a frontier pipeline is investigated individually. Escalations to Opus or GPT-6.1 inside P-D are part of the design
and are not avoided to save cost, but they are counted and reported.

A gate run that misses a threshold is not "close enough". Find the cause, fix it under a new item, and rerun the
affected pipelines.

## Prior decisions and archive constraints (RS-0.9)

Several items here touch ground earlier work already covered. Before starting any item, read its row below. A
"constraint" narrows how the item is done; a "reversal" changes a recorded decision and needs Joel's answer in the
table first. Abbreviations: HER `archive/HANDOFF_EXAMINATION_RESULTS.md`, FLF `archive/FIX_LANDING_FAILURES.md`, CI
`archive/CONCURRENCY_IMPROVEMENTS.md`, MH `docs/MODEL_HETEROGENEITY.md`, HDR `docs/HYBRID_DEVELOPMENT_RESULTS.md`, PHI
`archive/PROMPT_AND_HANDOFF_IMPROVEMENTS.md`, FCA/FCX `archive/FAILURE_CASES_ACTIONABLE.md` and `_CODEX.md`.

### Reversals (decided 2026-11-25)

The earlier decisions were sound for the goal they served, which was keeping frontier pipelines stable without
large refactors. This plan's goal is different, and in most cases the old reasoning does not survive it. Where it
does, the recommendation is to keep the decision and change the item.

| # | Items | Recorded decision | Recommendation | Decision |
|---|---|---|---|---|
| R1 | RS-3.1 to RS-3.4 | HER:660 markers in descriptions stay; HER:645 chose the `<title> [Worker]` lookup | Reverse. Typed columns are what make G2 and G3 testable | Reverse (Joel, 2026-11-25). Descriptions stay as written; history is rendered into prompts at launch and shown as an attempts timeline; migration backfills columns from old markers without rewriting old text |
| R2 | RS-3.5, RS-3.6 | HER:653 converting remaining full-row writes to compare-and-set is not planned | Reverse, keeping the `UpdateAsync` Cancelled guard and the Blocked edges (FCC FC-4.4) | Reverse as planned (Joel, 2026-11-25) |
| R3 | RS-4.5 | HER:655 dispatch callers not serialized; a global lock would slow every completion | Reverse the single-loop part only: a coalescing wake signal, never a lock in the completion path; completion never waits for dispatch | Approved as recommended (Joel, 2026-11-25) |
| R4 | RS-5.2 | HER:583, C6 (:460) arbitrary `runtime_error` stays Crash because retrying can repeat side effects | Change the item: `Unclassified` resumes in place only when the attempt ran no external side-effect tool (git push, network, process outside the dock); otherwise it escalates without a resume | Approved as recommended (Joel, 2026-11-25) |
| R5 | RS-6.6 | HER:457 (C3) do not strip formatting or select the last verdict | Keep C3. Drop "accept the last line" from RS-6.6; ambiguity goes to the format-only repair turn (RS-6.4) instead | Approved as recommended (Joel, 2026-11-25) |
| R6 | RS-6.5 | HER:717 strict matching, no automatic plan normalization; MH: two plan blocks are refused | Keep refusing two blocks and keep validation strict. Limit RS-6.5 to locating one block (bare or `~~~`) and better error detail; type coercion happens only through the repair turn | Approved as recommended (Joel, 2026-11-25) |
| R7 | RS-8.8 | HER:640 no per-runtime stall thresholds (delays catching real hangs) | Keep. Replace per-profile stall thresholds with streaming heartbeats (RS-7.7); per-profile limits only for first-token time and maximum runtime | Approved as recommended (Joel, 2026-11-25) |
| R8 | RS-8.6 | HER:609 least-recently-used rejected; name order kept; a fix must respect Judge independence | Proceed behind a setting, default off, and only with Judge independence preserved | Approved as recommended (Joel, 2026-11-25) |
| R9 | RS-8.1, RS-8.5 | MH: the profile does not set tier or change routing automatically; routing changes only after held-out production tasks | Keep. Probes propose capabilities and tier; an administrator accepts them in the profile UI (one click), and data-driven tier stays opt-in | Approved as recommended (Joel, 2026-11-25) |
| R10 | RS-11.1 | HER:656 separate services not created because the methods share mutable state | Reverse after Phases 3 to 6 remove the shared state (agrees with REFACTORING_PLAN "no partial classes") | Approved as recommended (Joel, 2026-11-25) |
| R11 | RS-2.6, RS-4.4 | FLF:219 dedup any dispatch whose description references a LandingFailed mission; CI:58 v3 ownership overlap only warns | Keep both: `RescueOfMissionId` is added in front of the description dedup, and overlap rejects only for v4 plans | Approved as recommended (Joel, 2026-11-25) |
| R12 | RS-4.4 | Older Architect plan formats (unversioned JSON, `[ARMADA:MISSION]` markers) drop a bad parent link, flatten a too-deep chain, and only log a warning | Reverse: every format goes through the one validator; a bad parent, self-reference, conflicting `parent`/`dependsOn`, or excess depth gets the Architect a repair turn with typed errors, then fails as InvalidOutput | Approved as recommended (Joel, 2026-11-25) |

### Constraints by item

- RS-1.1: `StubCaptainRuntime` fault scripts and `Services.RuntimeFaultMatrix` already fake the runtime process; the
  simulator fakes the model server below it. Extend those for process-level faults instead of adding another fake.
- RS-1.4: record into `StageOutcome` (it already holds provenance, suspected cause, `ContractRepairKind`, escalation
  links), keep observed failure separate from suspected cause, and add Judge finding categories, tool failure kinds,
  and per-turn input tokens (HDR: qwen3-coder used 15.2M input tokens resending context).
- RS-1.7: already done; `judge-negative` with a `seededSet` grader is in all 8 packs.
- RS-2.2: keep `deepseek-v4.1-flash` and API captains at Standard (HER D12, C10); 8B and below stay Economy.
- RS-2.7: update the orphan-claim race test (CI:33); re-pend without charge only when the branch has no commits.
- RS-2.11: a missing dock stays `Infra` (FLF:212-215).
- RS-2.12: `SPEC_CONFLICT` recovery still requires the quoted `## Spec Conflict` section.
- RS-4.6: `MissionAdmissionEvaluator` already runs Parent, BroadScope, VesselVoyageSlot, VoyageMissionSlot, BranchLock
  with fairness over eligible missions; keep them and the `admission.stuck` meaning; escalation eligibility differs
  on purpose (HER D18).
- RS-4.9: endpoint and runtime caps are only enforced under the lock today (CI:401); re-validate them inside the
  claim, keep vessel-then-capacity lock order and the deadlock retries.
- RS-4.10: PID reuse needs a start-time match; gate a Harbor `LaunchJobId` behind a capability flag.
- RS-5.1: suspected-cause rules stay administrator data; `FailureKindMapper` maps observed kinds only.
- RS-5.5: `MaxOutputRepairAttempts` (0 to 3) and `TransientResumeDelaySeconds` exist; per-profile policy cannot
  shorten the FullPipeline rework chain.
- RS-5.6: escalation returns false for strict assignments (HER:630); strict missions are excluded from G12.
- RS-5.8: rescue stays limited to standalone work stages, and its brief keeps "integrate commit X".
- RS-6.1: generated contracts replace only templates the operator has not edited.
- RS-6.4: review-gated stages are never repaired.
- RS-6.8: citing an inspected file stays sufficient (the Codex Judge could not run dotnet in its sandbox).
- RS-6.12: list removal for 2.0; removal is only allowed in a major release (CI D-026).
- RS-7.2: never execute calls recovered from a failed or incomplete stream (HER HF-24).
- RS-7.7: default streaming on only where the Harbor reports `api-inference-streaming-v1`.
- RS-7.8: served context is part of profile identity; a discovered context creates a new profile revision, and
  discovery runs on the serving Harbor (HER D14).
- RS-7.9: keep the default read cap of 200 lines and 24,000 characters (HF-06); scale only the hard maximums.
- RS-8.3: measure the lean template on the battery before switching to it automatically (MH principle 1).
- RS-8.7: model-identity Judge exclusion can stall two-captain pools; fall back to the typed blocker, not a stall.
- RS-9.1: re-verify on each push attempt and end as `LandingConflict` with both heads; reuse `MaxLandingRetries`
  (default 3) or name a new setting deliberately.
- RS-9.2: keep deferred LocalMerge retry, auto-reconcile, and dock repair.
- RS-9.3, RS-10.4: concurrent fetches per repository are already combined and branch paths cached for 30 s; the
  remaining work is the per-voyage advisor context.
- RS-12.10, RS-12.13: use `useResourceTable`, `PageHeader`, and `RecordDetailModal` (SIMPLIFICATION).

### Deferred work the plan does not cover

Recorded for completeness; each goes to `docs/BACKLOG.md` with a reason unless it is pulled in later: PHI R18
(Usability reproducibility), R19 escalation when evidence is inaccessible, R20 and R21 (Recorder, with HF-23), R22
(linter split), R25 (Usability viewports); FCA T3-4 integration worktree for LocalMerge; FLF C3 (ordering-language
lint), C4 (repeated-papercut escalation), per-voyage integration branches; HER HF-05 (semantic plan validation), HF-11
(transcript), HF-14 (shell exit masking), R6 load timeout (partly RS-7.16); MH 3.5 translator, Decision 4 tool
guidance, Phase 6.3 persona-scoped quarantine; FR R5 production TODOs (MCP enumerate with `includeDescription`
ignores filters and tenant scope; WebSocket `command.error` has no code). The tenant-scope TODO is a security issue
and is pulled in as RS-2.13.

---

## Phase 0: Setup and baseline

Nothing in later phases can claim an improvement without a before measurement, and CI must be green before the
branch starts adding tests to it.

- [x] **RS-0.1** Branch. `feature/v1.5.0` created from `main` at `a2503b7a2`. Confirm `git status` shows only the
  untracked `assets/vscode/`. Evidence: branch pushed to origin with the plan in `3c0a9086a`; status showed only
  `assets/vscode/` before the first commit.
- [x] **RS-0.2** Version 1.5.0 (authorized 2026-11-25). Update every version location listed in the runbook section
  15 and `src/Test.Shared/Suites/Services/ReleaseVersionSuite.cs`, following the reference bump commit `c3f5dc176`.
  Add `## Unreleased` entries for 1.5.0 work as it lands; the release heading is written in RS-15.2. Mobile:
  `app.config.ts` version 1.5.0 (build numbers are handled in RS-16.4). Evidence: same 26 files as `c3f5dc176`
  (the `docs/UPGRADING.md` section is written with the migration in RS-13.6); `CHANGELOG.md` heading is
  `## v1.5.0 (unreleased)` per the 1.4.0 convention; Grafana image tag left at 11.4.0.
- [x] **RS-0.3** [DECISION] Confirm or change the acceptance gate thresholds G1 to G12. Answer (2026-11-25): G1 at
  99%; G2 to G12 as drafted.
- [x] **RS-0.4** [DECISION] Reference configuration for the open models. Answer (2026-11-25): Qwen3.8 27B on the
  ApiEndpoint runtime, Ollama `/api/chat` format with a bearer token, served through the maintainer's Conductor
  instance. Additional families that must pass: DeepSeek 4.1, GLM, and inkling-2-mini, through Conductor, with
  Ollama Cloud as the fallback for DeepSeek, GLM, and Kimi. Frontier: Opus on Claude Code, GPT-6.1 on Codex.
  Connection details are in the git-ignored local file (see Definitions).
- [-] **RS-0.10** Endpoint inventory. Read the Conductor source (`~/Code/Conductor`, its `REST_API.md` and Postman
  collection) and use its API to list every model it hosts, with served context length, quantization, and tool
  support. Fill in the model path segments in `benchmarks/config/gate-endpoints.local.json` (local file only), and
  confirm each reference model answers a chat request with a tool call through Conductor, using Ollama Cloud only
  for families Conductor does not host. Record in the validation log which models are available and where, without
  hosts or tokens. Evidence: (2026-11-25) Conductor lists 42 runners; qwen3.8:27b answered a native
  tool call (262,144 context, Q4_K_M). inkling-2-mini timed out at Conductor's 300 s limit twice, after which
  Conductor reported "No healthy endpoints", and later the same 502 for qwen3.8 (both runners appear to share one
  backend). Ollama Cloud models picked from a free listing: deepseek-v4.1-flash, glm-5.3, kimi-k3. First live gate run
  (Gate-D, csharp, 1 run) stopped on that outage: Armada classified the 502 as Transient and relaunched about ten times
  over the outage grace, each attempt waiting out the proxy timeout (up to about 10 minutes), then failed the stage as
  Infra and halted the voyage. Inputs for RS-7.16 (cold loads, proxy time-to-first-byte) and RS-8.8 (timeouts).
- [ ] **RS-0.11** Register the reference models in a development Admiral as model endpoints and captains (Qwen3.8,
  DeepSeek 4.1, GLM, inkling-2-mini on ApiEndpoint; Opus on Claude Code; GPT-6.1 on Codex), run the existing probes
  and battery once per model, and save the scorecards as the 1.4.0 starting picture for RS-0.8. Evidence:
- [x] **RS-0.5** Fix the four failing `ci.yml` cases recorded in the runbook section 14 ("SDK preserves passive capture
  observations and their original timestamps" and three "real pi run" cases that need the `pi` CLI; the latter should
  skip with a reason when the CLI is absent, not fail). Fix the `full.yml` compose job's missing
  `ARMADA_GRAFANA_ADMIN_PASSWORD`. Evidence: `fab7099ca` (Touchstone 0.2.1 runs a case with a skipReason unless
  `skip` is also true: five suites fixed, `Models.TestSkipFlagHygiene` guards it, real pi runs opt-in with
  `ARMADA_TEST_REAL_PI=1`; passive capture compares whole seconds), `ae8057a80` (Grafana password in compose
  validation), `b29365f37` (TUI focus sweep: stale placements below nested scopes; all 658 TUI cases pass).
- [ ] **RS-0.6** Baseline run of everything: `dotnet build src/Armada.sln -c Release -warnaserror` (both frameworks),
  `Test.Automated` net8.0 and net10.0, xUnit and NUnit adapters, dashboard, mobile, TUI parity, VS Code extension.
  Record counts in the validation log. Evidence:
- [ ] **RS-0.7** Baseline performance with `Armada.Benchmark` (admission runner and dispatch latency) and the
  `benchmarks/run-baseline.sh` flow, saved under `benchmarks/baselines/1.4.0/`. Evidence:
- [ ] **RS-0.8** Baseline reliability: after RS-1.6 exists, run the four reference pipelines on 1.4.0 code (a
  worktree at `a2503b7a2`) with the same battery and record G1 to G12. This is the "before" column of the release
  notes. Evidence:
- [x] **RS-0.9** Read the archive docs the review did not fully cover (`archive/FAILURE_CASES_*.md`,
  `archive/FIX_LANDING_FAILURES.md`, `archive/FRAGILITY_REMEDIATION.md`, `archive/SIMPLIFICATION.md`,
  `archive/REFACTORING_PLAN.md`, `archive/DISPATCH_ISSUES.md`). For each item in this plan that they already
  cover, add a note "see archive/X.md#section" so prior decisions are not undone by accident. Evidence: recorded in "Prior decisions and archive constraints"; Joel decided R1 to R11 on
  2026-11-25 (all as recommended).

## Phase 1: Test and measurement harness

The fixes in later phases are mostly about rare paths: a crash between two writes, a model that prints its tool call
instead of calling it, a 429 from a local proxy. Those paths need to be reproducible on demand before they are fixed.

- [x] **RS-1.1** Open-model behavior simulator. Extend `postman/simulations` (or add an in-process fake under
  `Test.Shared`) to an OpenAI-compatible and Ollama `/api/chat` fake that can be scripted per request to produce:
  Hermes/Qwen `<tool_call>` text instead of `tool_calls`, `<function=...>` text, fenced JSON tool calls, malformed
  tool-argument JSON, Ollama's malformed-tool-call HTTP 500, an unknown tool name, the same tool call repeated,
  reasoning without the opening `<think>`, a 429 without quota information (backpressure), a 429 with
  `insufficient_quota`, "llama runner process has terminated", "model requires more system memory", a
  context-overflow 400, a response cut by `length`, slow streaming (configurable tokens per second), and no streaming.
  Document it in `postman/README.md`. Evidence: in-process `OpenModelSimulator` (`src/Test.Shared/Infrastructure/OpenModel`), suite
  `Runtimes.OpenModelSimulator` 23/23; documented in `docs/TESTING.md` "Open-model simulator" rather than the
  Postman README because it runs in-process.
- [x] **RS-1.2** Scripted model outputs for contract tests: canned Architect plans (fenced, unfenced, `~~~`, prose
  inside the fence, draft then final, `"parent":"1"`, `"kind":"Implementation"`), Judge outputs (verdict in bold,
  verdict under a heading, two verdicts, verdict inside a truncated fence, `SPEC_CONFLICT` near-miss), Worker outputs
  (marker in backticks, marker in bold, missing marker). Stored as fixture files under `Test.Shared`. Evidence: `ModelOutputFixtures` (22 outputs, C# catalog in
  `src/Test.Shared/Infrastructure/ModelOutputs`, expected results follow decisions R5 and R6) and
  `Services.ModelOutputContract`: 19 read as intended today; `~~~` fences and bare JSON plans (RS-6.5) and a verdict
  after a truncation-cut fence (RS-6.6, confirming the review's suspicion) are skipped until those items land.
- [x] **RS-1.3** Crash-point injection. A test-only `ICrashPoints` service (no-op in production builds) with named
  points at every multi-write sequence this plan touches: merge queue push / entry Landed / mission Complete;
  incident create / FailureKind set / rescue dispatch; voyage row / mission rows / first assignment; Architect
  fan-out steps; stage handoff steps; launch / PID recorded. A harness kills and restarts the Admiral at a chosen
  point and checks the end state. Evidence: (2026-11-25) `CrashPoints` hooks at 12 points
  (`CrashPointNames`); the harness freezes the flow at the point instead of throwing, because the merge queue's
  catch-all handled a thrown crash as an ordinary failure. `Services.CrashConsistency`: merge queue cases reproduce
  RS-2.5 (entry stuck Testing) and RS-2.3 (mission stuck WorkProduced); recovery cases reproduce RS-2.6 (a duplicate
  incident after a crash past incident creation; an orphaned rescue the incident never records); the voyage case
  reproduces RS-4.3 (a voyage left Open with 1 of its 3 missions, forever). All five are skipped until those items
  land. The handoff and fan-out cases pass today (`RecoverDanglingHandoffsAsync` prepares the remaining dependent and
  creates the missing slice once, found by title) and guard RS-3.3. The launch case is written with RS-4.10, which
  builds the launch intent it needs to check.
- [x] **RS-1.4** Pipeline reliability metrics recorded per attempt (persisted, not in memory): runtime failure kind,
  repair kind and outcome (in-session, format-only, full rerun), escalation from and to (captain, model, tier),
  human interventions, dispatch latency. Exposed through Prometheus metrics per `TELEMETRY_REQUIREMENTS.md` and
  queryable for the gate report. Surfaces: Server, Grafana. Evidence: no migration was needed. Per attempt, `StageOutcome` already holds
  failure kind, runtime failure kind, disposition (`OutputRepair` is a full rerun), deterministic repair kind, the next
  captain, and timing; tokens come from `TokenUsageRecord` (Source "mission", SourceId, CreatedUtc in the attempt
  window); dispatch wait from `MissionWaitInterval` and `armada_missions_queue_seconds`. Added
  `armada_stage_outcomes_total` (persona, runtime, model, disposition, failure kinds, repair) and documented it in
  `docs/TELEMETRY.md`. Human interventions are not separate rows: the gate runs unattended, so any mutating
  operator request during a gate run (request history) counts; product-facing intervention counts come with
  RS-12.13. New repair modes (in-session, format-only) become `StageDispositionEnum` values in RS-6.4, and tool-call
  failure counts arrive with Phase 7.
- [x] **RS-1.5** Gate report generator: a `Armada.Benchmark` runner (or `armada battery report`) that reads RS-1.4
  data for a set of battery runs and prints G1 to G12 with pass or fail, as Markdown and JSON, into
  `benchmarks/results/`. Evidence: `src/Armada.Gate` (own project: Armada.Benchmark is black-box
  by design) sets up roles, endpoints, captains, and pipelines idempotently through the SDK, runs each reference
  pipeline as a pipeline-mode battery run, and writes `benchmarks/results/gate-<time>.md|json`. It scores G1, G2, G4,
  G5, G6; G3 and G7 to G12 report NotMeasured with the suite that checks them (crash suite, admission benchmark,
  metrics, and records that later items add). `Models.GateScoring` covers the scoring. `start-bench-server.sh`
  gained `--real-agents`. Documented in `benchmarks/README.md`.
- [x] **RS-1.6** Reference pipelines, seeded as built-in example pipelines (disabled by default) and as battery run
  presets. Opus always runs on Claude Code, GPT-6.1 on Codex, and every open model on ApiEndpoint. The gate harness
  reads endpoint details from `benchmarks/config/gate-endpoints.local.json` and fails with a clear message when the
  file is missing:
  - **P-A** Architect Opus, Worker Qwen3.8, Test Engineer Qwen3.8, Judge GPT-6.1.
  - **P-B** Architect GPT-6.1, Architect fan-out to Qwen3.8 Workers, Judge Opus.
  - **P-C** Architect Qwen3.8, Worker Opus, Judge Qwen3.8 with the tier rule from RS-8.7 (proves the rule routes the
    final judgment to a frontier model or flags it).
  - **P-D** Every persona Qwen3.8, escalation ladder Qwen3.8 -> GPT-6.1 -> Opus (RS-5.6).
  Evidence: Battery runs gained a pipeline mode
  (`BatteryRunRequest.Pipeline` and `CaptainOverrides`, REST, MCP `run_battery`, SDK through the shared model, Postman
  "Run Battery Under a Mixed Pipeline"): each Worker task runs as a voyage through every stage of the named pipeline,
  staffed per persona, graded on the final branch; the run records pipeline and staffing in its configuration.
  `Services.Battery` covers it. The reference pipelines are not product seeds after all: no built-in pipeline is
  Architect, Worker, Test Engineer, Judge, and the staffing (not the stages) is what differs between P-A and P-D, so
  the gate harness (RS-1.5) creates its pipelines and captains in its isolated Admiral from the committed preset file
  `benchmarks/config/reference-pipelines.json` (roles only: Gate-A to Gate-D plus Gate-D variants for inkling,
  DeepSeek, GLM, Kimi, sized to the run budget) and the local endpoint file.
- [x] **RS-1.7** Seeded-defect tasks in at least the `csharp`, `python`, and `javascript` battery packs so G7 can be
  measured (a task whose correct verdict is FAIL). Evidence: already present: `judge-negative` with a `seededSet`
  grader in all 8 `battery/*/battery-pack.json` (RS-0.9).

## Phase 2: Correctness bugs found in review

Each of these is a confirmed defect in 1.4.0. They are small, and each gets a regression test that fails first.

- [x] **RS-2.1** Findings severity. `HandoffArtifactBuilder.ParseSeverity` (`HandoffArtifactBuilder.cs:382`) checks
  `low|minor|nit|info` before blocking terms with no word boundaries, so "High - follows from", "Blocking: allows",
  and "Critical, no information" become NonBlocking. Use `\b` boundaries and check blocking terms first. Test with
  all three strings. Evidence: whole-word severity regexes in `HandoffArtifactBuilder`; `Services.HandoffArtifact`
  `severity_words_match_whole_words`.
- [x] **RS-2.2** Tier classification. `CaptainTierSelector.cs:27`: Premium pattern `gpt-?5(\.\d+)?` misses GPT-6.x
  and later; `o3|o4` unanchored; `gemini-\d*.*(?!pro)` lookahead is a no-op. Make the GPT pattern version-agnostic
  for 5 and above, anchor `o3`/`o4`, rewrite the Gemini rule. Table-driven tests for at least 30 model names
  including `gpt-6.1`, `gpt-6.1-mini`, `qwen3.8`, `qwen3.8:32b`, `claude-opus-5-5`. Remove the test-only
  `CaptainTierSelector.Select`. Evidence: Premium pattern accepts GPT 5 and later, anchors o3/o4, needs Gemini `-pro`; Economy is still
  checked first (mini, nano, flash stay Economy; archive D12 kept). `Services.CaptainTier` `classify_model_table`
  (33 names). Removing the test-only `CaptainTierSelector.Select` moves to RS-11.2 (ten test call sites).
- [x] **RS-2.3** Mission stranded after landing. `MergeQueueService.LandEntryAsync` (lines 690 to 745) persists the
  entry as Landed and then completes the mission in a second write; `FlagMissingMergeHandoffsAsync`
  (`AdmiralService.cs:2236`) skips any mission with a merge entry. Write both in one transaction, and add a sweep
  that promotes WorkProduced or LandingFailed missions whose latest entry is Landed. Crash-point test (RS-1.3).
  Evidence: each queue pass first completes WorkProduced and LandingFailed missions whose latest entry is
  Landed (compare-and-set, no lease needed); a separate sweep was chosen over one transaction across the push and two
  rows because the push itself cannot be in a transaction. The `Services.CrashConsistency` case is now on and passes.
- [x] **RS-2.4** Planning sessions lose the objective. `PlanningSessionCoordinator.cs:407` sets the voyage
  description to "Created from planning session <id>", which Architect slices then use as the objective. Store the
  drafted description; keep the link in `SourcePlanningSessionId`, set in the create request rather than a second
  update. Evidence: the voyage description is the drafted objective; `E2E.PlanningSession` asserts it. Moving
  `SourcePlanningSessionId` into the create request belongs to RS-4.1.
- [x] **RS-2.5** Merge queue lease. `_ProcessLeaseDuration` (15 minutes, `MergeQueueService.cs:76`) is acquired once
  per pass and never renewed, so a long pass can be reset and processed twice. Renew per entry (heartbeat) and make
  the duration a setting. Test with a slow verification fake. Evidence: `MergeQueueLeaseSeconds` (default 900, 5 to 14400) replaces the constant; renewed before each entry;
  losing it stops the pass. The `Services.CrashConsistency` case is now on (5-second lease) and passes.
- [x] **RS-2.6** Rescue incidents not crash-consistent. Pass `FailureKind` in the incident create request; before
  dispatching a rescue, look up existing rescues by `RescueOfMissionId` in any status and adopt them; drop the
  title and substring dedup (`ReferencesOriginal`). Crash-point tests. Evidence: the incident is created with `FailureKind`; an Armada rescue
  (`IsRescue`) of the mission that the incident does not reference is adopted in any status before a new one is
  dispatched; an operator's re-apply still suppresses without spending budget, and the title and description dedup
  stays behind it (decision R11). Both recovery cases in `Services.CrashConsistency` are on and pass.
- [x] **RS-2.7** Startup burns retries. `CleanupStaleCaptainsAsync` (`AdmiralService.cs:1007`) treats an Assigned
  mission with no process as an interrupted run and spends `RedispatchAttempts`. Return never-launched Assigned
  missions to Pending without charging an attempt. Evidence: startup cleanup returns an Assigned mission with no process and no
  start time to Pending through a compare-and-set without charging, as orphan recovery does; such a mission ran no
  agent, so its branch has no commits from it (archive CI:33 constraint). The orphan-claim race test in
  `Services.SequentialDispatch` now requires zero attempts; it fails on the old code (1) and passes.
- [x] **RS-2.8** Architect slices may lose playbooks. Verify whether slices 2..N get playbook snapshots
  (`LoadMissionPlaybookSnapshotsAsync`); fix by routing slice creation through the creation service (RS-4.1).
  Regression test either way. Evidence: confirmed (a mission reads only its own snapshots; slices 2..N and
  their cloned stages had none). Fan-out now copies the template's snapshot content (not its per-mission paths) onto
  each created slice and stage. `Services.ArchitectFanOutPlaybooks` fails on the old code ("Bravo [Worker]" has 0) and
  passes. Routing slice creation through the creation service remains part of RS-4.1.
- [x] **RS-2.9** Voyage ownership and tenancy on REST. Voyages take `UserId = vessel.UserId` rather than the caller;
  REST does not check the vessel against the caller's tenant before dispatch while MCP does
  (`McpCallerScope.ReadVesselAsync`). Confirm with tests for a non-admin caller and a cross-tenant vessel; fix
  through RS-4.1. Evidence: confirmed and fixed: tenant B got 201 for a voyage with missions on
  tenant A's vessel and for a mission on tenant A's vessel or voyage. `POST /api/v1/voyages` and `POST /api/v1/missions`
  now resolve the vessel (and voyage) through `McpCallerScope`, returning 404. `E2E.CrossTenantApi` has two new cases
  (both fail on the old code). WebSocket create commands are global-admin only by design. The ownership half (voyage
  `UserId` taken from the vessel) only matters for a tenant admin dispatching on another user's vessel and moves to
  RS-4.1, which passes the caller to voyage creation.
- [~] **RS-2.10** `KnownErrorTypes` lists `"error"` and `"turn.failed"` but `ClassifyErrorType` returns null for
  them, so a Codex error without a status becomes Crash. Map them. Evidence: superseded by RS-5.2. The status-less `error` and `turn.failed`
  fall to Crash on purpose (archive C6: retrying can repeat side effects); under decision R4 they become
  `Unclassified` with a resume only when no side-effect tool ran, which is RS-5.2's job.
- [~] **RS-2.11** `LandingFailureClassifier.IsProcessStartFailure` treats any `IOException` as a process-start
  failure. Narrow it to the start path. Evidence: no behavior defect: `IsProcessStartFailure` has one caller,
  which records Infra for every non-conflict failure either way, so the broad `IOException` match changes no outcome
  (and a missing dock stays Infra, per FLF:212). Its message-text match is replaced by typed detection in RS-5.11.
- [x] **RS-2.12** `ParseSeverity` style issue aside, `ContractRepair.TryRecoverVerdict` maps `SPEC_CONFLICT` to
  None (around `ContractRepair.cs:115`). Recover it like the other verdicts. Evidence: near-miss SPEC_CONFLICT recovered only with a non-empty `## Spec Conflict` section (the verdict
  line itself does not count); canonical line added. `Services.ContractRepair` and a new
  `Services.ModelOutputContract` fixture.

- [x] **RS-2.13** MCP enumerate with `includeDescription` ignores filters and tenant scope (FR R5 production TODO).
  Confirm with a cross-tenant test and fix. Evidence: already fixed since the archive note: both enumerate branches use
  `EnumerationScope.EnumerateScopedAsync`, and `E2E.McpTenantIsolation` checks every filter with includeDescription on
  and off plus tenant isolation (8/8 pass, 2026-11-25). No change needed.

## Phase 3: Typed state and an enforced state machine

Text markers in descriptions route missions today. A user brief that happens to contain "## Escalated to " pins
routing; a renamed Architect slice breaks fan-out recovery. Everything a program reads goes into a column.

- [x] **RS-3.1** Migration 133 (all four providers): on missions add `EscalationCount`, `EscalatedFromCaptainId`,
  `EscalatedFromModel`, `OutputRepairCount`, `FanOutCompletedUtc`, `ArchitectSourceMissionId`, `ArchitectPlanIndex`,
  `LaunchIntentUtc`, `LaunchJobId`; on merge entries nothing new unless RS-9 needs it. Backfill from the existing
  text markers so upgraded databases keep their state. Upgrade test from the v1.4.0 baseline with missions in every
  marker state. Evidence: `MissionTypedStateSchema` (migration 133) adds
  the columns and backfills escalation count, fan-out completion, and slice source and plan index from the markers.
  `OutputRepairCount` already existed as `OutputRepairAttempts` and was not duplicated. Old descriptions are not
  rewritten. Suite `Database.MissionTypedStateUpgrade` (upgrade from schema 132 with every marker state, replay, and
  round-trip) passes on SQLite, PostgreSQL, MySQL and SQL Server (2026-11-25). Fields reach REST and MCP through the
  mission model; dashboard, TUI and SDK display follows in the later surface items.
- [x] **RS-3.2** Replace `"## Escalated to "` detection (`AdmiralService.cs:2854`, `MissionService.CaptainRouting.cs:77`
  and `:281`, `MissionService.WorkerEnvelope.cs:106`) with the columns. The escalation note can stay in the prompt
  rendering; nothing reads it back. Evidence: the three routing checks and the escalation guard read
  `EscalationCount`; escalation records `EscalationCount`, `EscalatedFromCaptainId` and `EscalatedFromModel`. The
  note is still appended for the prompt until RS-3.4 and nothing reads it back. `Services.AdmiralService`
  `stuck_local_worker_escalates_to_planner` now uses a brief that quotes the old heading, and asserts the columns.
- [x] **RS-3.3** Replace `<!-- ARMADA:ARCHITECT-HANDOFF -->`, the fan-out-complete marker, and title-based slice
  lookup (`Title == parsed.Title + " [Worker]"`) with `FanOutCompletedUtc` and (`ArchitectSourceMissionId`,
  `ArchitectPlanIndex`). Evidence: the fan-out writes `ArchitectSourceMissionId` and
  `ArchitectPlanIndex` on every slice and `FanOutCompletedUtc` on the Architect; readiness, recovery and plan
  dependencies find slices through `FindArchitectSlice` (source and index, newest live slice). Both markers and the
  title lookups are gone. `Services.PipelineDispatch` `interrupted_architect_fanout_is_finished_without_duplicates`
  now renames a slice before recovery and asserts it is not recreated.
- [x] **RS-3.4** Mission description is immutable after creation. Handoff text, rework findings, repair notes, and
  escalation notes are rendered at launch from typed records (Phase 6) rather than appended to `Description`. Remove
  `StripPipelineHandoff` and its duplicated `SectionMarker` literal. Keep reading old markers only in the RS-3.1
  backfill. Evidence: `Mission.Notes` (one `MissionNote` per
  `MissionNoteKindEnum`, stored in `notes_json`, migration 133) holds the pipeline handoff, reviewer guidance, review
  feedback, prior Judge findings, output repair, resume, escalation and integration merge text; `MissionNotes`
  sets, removes and renders them, and the prompt and rescue brief use `MissionNotes.EffectiveDescription`.
  `StripPipelineHandoff`, `ReplaceOutputRepairNote`, `ApplyReviewFeedback`, `ApplyReviewerGuidance`,
  `RemoveIntegrationMergeSection` and their marker constants are removed. One exception, by design: an Architect
  fan-out still defines the waiting pipeline placeholders (first slice and its stage chain) by writing their title
  and description once, because the plan is their definition. A mission in flight across the upgrade may show an
  old handoff in its description and the new one as a note. `Database.MissionTypedStateUpgrade`
  `notes_keep_description_immutable` passes on all four providers.
- [x] **RS-3.5** One transition API. `MissionTransitions.TryTransitionAsync(mission, allowedFrom, to, mutate, token)`
  that checks `MissionStateMachine`, does the compare-and-set, emits the event, and triggers voyage re-evaluation.
  Add the real internal edges to the table (Assigned -> Pending, InProgress -> Pending for escalation,
  InProgress -> Verifying). Evidence: `MissionTransitions` (one instance shared by every service, wired in
  `ArmadaServer`) drops any allowed status with no legal edge, writes with `TryUpdateIfStatusAsync` or
  `TryUpdateIfUnchangedAsync`, restores the stored status on a refused write, emits `mission.status_changed` once per
  change (the black box capture and the operator routes no longer emit it), and re-evaluates the voyage at a terminal
  status. `MissionStateMachine.IsValidInternalTransition` is the internal table (a superset of the operator table,
  listed in docs/PIPELINES.md); operator surfaces (REST, MCP, WebSocket) still use `IsValidTransition`. Two latent bugs
  fixed on the way: a stage approved in review whose handoff failed was never marked Failed (the full-row guard
  refused Complete -> Failed), and a cancel of a Complete mission is no longer attempted. `Services.MissionTransitions`
  (7 cases) passes on SQLite, PostgreSQL, MySQL and SQL Server.
- [x] **RS-3.6** Move all 65 direct `.Status = MissionStatusEnum.X` writes in `Armada.Core` and `Armada.Server` to
  RS-3.5. Add a test that scans source for direct writes outside the transition class and fails if any return.
  Includes the coordinator's direct `unresolved.Status = Complete`. Evidence: all 66 regex matches plus the 8 the
  regex missed (conditional and variable targets, the operator routes, the agent status line, the merge queue,
  vessel deletion) go through `MissionTransitions`; 34 of them were unconditional full-row writes. Only the database
  layer writes status directly: row readers, the admission compare-and-set in `CaptainSlotStore`, and startup
  migrations. `no_direct_status_writes` scans Armada.Core and Armada.Server and fails on any other write. Full suite
  on SQLite: 7425 tests, 0 failures after the fix, 39 skipped. The admiral, pipeline, review, handoff, landing and
  recovery suites (316 tests) pass on PostgreSQL, MySQL and SQL Server except two pre-existing `AdmiralService` cases
  that open a SQLite connection directly and cannot run on a server database.
- [x] **RS-3.7** Persist in-memory recovery state that must survive a restart: endpoint breakers, launch backoff,
  `_TransientOutageStarts`, escalation cooldowns (`EscalationService`). Evidence: one `recovery_state` table
  (migration 134, all four providers, one shared SQL class) behind `RecoveryStateStore`; each service writes through
  on change (a clean run on a clean breaker and a successful launch write nothing) and `ArmadaServer` reloads all four
  before the listener and health check start. Escalation cooldowns stay on the monotonic clock in memory and are
  placed on it from the stored wall-clock fire time at load. `Services.RecoveryState` (5 cases: store round trip and
  one restart case per state) passes on SQLite, PostgreSQL, MySQL and SQL Server; full SQLite suite 7430 tests, the
  one failure a race in `E2E.BlackBox` (brief read before record.json), fixed in the test.

## Phase 4: One path for voyage creation and dispatch

- [x] **RS-4.1** `VoyageCreationService` in Core taking an auth context and one `VoyageDispatchRequest`. It owns
  validation, pipeline resolution (a standard voyage is a pipeline with one stage), playbooks, hierarchy, ownership,
  and the dispatch advisor. REST (`Routes/VoyageRoutes.cs:190`), MCP (`McpVoyageTools.cs:150`), WebSocket
  (`WebSocketCommandHandler.cs:424`), planning, and Architect fan-out all call it. Removes the four public overloads,
  `DispatchStandardVoyageAsync`, and the duplicated preamble in `AdmiralService.cs:257-540`. Fixes the drift (MCP
  `LandingMode`, tenant fallback, objective linking through `ObjectiveService.LinkVoyageAsync`). Surfaces: REST,
  MCP, WebSocket, SDK. Evidence: `VoyageCreationService.CreateAsync(caller, VoyageCreationRequest)` (exposed as `IAdmiralService.CreateVoyageAsync`) is the only creation path for REST, MCP, WebSocket, planning, fleet actions and battery runs; the four overloads and both creation loops are gone (a standard voyage is one stage). The voyage belongs to the caller (RS-2.9 ownership half), MCP bare voyages keep `LandingMode`, objective linking goes through `ObjectiveService.LinkVoyageAsync` before returning (an unknown objective creates nothing), and `SourcePlanningSessionId` is set in the create request (RS-2.4). The SDK request gains `ObjectiveId`, `CaptainAssignments` and `LandingMode`. New `Services.VoyageCreation` (5 cases) passes. Architect fan-out slice creation moves onto this path with RS-4.3, which makes it one transaction.
- [x] **RS-4.2** REST `MissionRequest` gains `Persona` and `Mode` to match MCP. Surfaces: REST, SDK, Postman,
  dashboard voyage form, mobile, TUI, CLI. Evidence: `MissionDescription.Persona` and `Mode` are part of the wire format
  (MCP's advertised `persona` was dropped by `[JsonIgnore]`; MCP `dispatch` also gains `mode`); REST `MissionRequest`,
  SDK `DispatchRequest`, dashboard and mobile voyage forms (shared `voyageForm.ts`), TUI `VoyageCreateScreen`,
  `armada voyage create --persona --mode`, Postman "Dispatch Persona and Mode Voyage", `REST_API.md` and `MCP_API.md`.
  Tests: `E2E.MissionPersonaMode` (REST, MCP, SDK; bad mode is 400), `voyageForm.test.ts`, `VoyageCreate.test.tsx`.
- [x] **RS-4.3** Atomic creation. Create the voyage (status `Building`) and all mission rows in one transaction, then
  move it to `Open` and run one assignment pass. No captain launches before every stage of the voyage exists. Same
  for Architect fan-out (one transaction for all slice rows, rewiring, and ownership) and stage handoff (reuse the
  `CreateChainAsync` pattern). Crash-point tests. Evidence: voyage creation done (RS-4.3a): `VoyageStatusEnum.Building`,
  missions written with one `CreateChainAsync`, `TryAssignAsync` holds missions of a Building voyage, health phase
  `FailInterruptedVoyageCreationsAsync`, advisor overload with planned missions; `Services.VoyageCreation`
  (`nothing_admitted_while_building`, `interrupted_creation_fails_on_health_check`, `advisor_sees_earlier_missions`) and
  `Services.CrashConsistency` crash cases at `voyage.created`, `voyage.mission-created`, `voyage.opened`,
  `voyage.mission-assigned` (the pending voyage crash case now runs). Fan-out done (RS-4.3b): new
  `IMissionMethods.WriteBatchAsync` (inserts, status-guarded full-row updates and playbook snapshots in one transaction on
  all four providers; a failed guard rolls everything back), the fan-out helpers build an `ArchitectFanOutWork` in
  memory and `CommitArchitectFanOutAsync` writes it with the Architect's `FanOutCompletedUtc`; recovery of a pre-change
  partial fan-out uses the same path. Crash points `fanout.planned` and `fanout.written` replace the three per-row
  points; `Database.MissionDatabase` `mission_write_batch_is_all_or_nothing`. Handoff done (RS-4.3c): every dependent
  stage's branch and handoff note is written with one `WriteBatchAsync`, each guarded on Pending; crash points
  `handoff.planned` and `handoff.written` replace `handoff.dependent-updated`, and the crash cases check both dependents
  were prepared or neither.
- [x] **RS-4.4** One hierarchy and ownership validator: a pure function over (index, parent, dependsOn, ownedPaths)
  used by REST, MCP, Architect v3/v4 plans, and legacy plans, with one outcome per rule (reject with a typed error
  list). Ownership overlap is checked for all dispatch paths. Replaces `MissionHierarchyPlan.Resolve`,
  `ArchitectPlanParser.ValidateHierarchy`, the silent flattening in `Normalize`, and the warning in
  `ApplyArchitectMissionDependenciesAsync`. Evidence: `MissionPlanValidator.Validate(IReadOnlyList<PlanNode>, maxDepth,
  overlapIsError)` returns `PlanValidationResult` with `PlanFinding`s typed by `PlanFindingCodeEnum` (11 rules) and the
  resolved parents and depths. Callers: `VoyageCreationService` (REST and MCP; `MissionHierarchyPlan` deleted),
  `ArchitectPlanParser` for v2 to v4 and unversioned plans (checked against the Architect's own numbering before
  dropped entries are removed), and `MissionService.ValidateArchitectHierarchy` for every parsed plan at handoff,
  including `[ARMADA:MISSION]` markers, whose "Depends on:" text is resolved to a position at parse time. A rejected
  plan takes the existing output repair turn (R12). `ApplyArchitectMissionDependencies` no longer resolves text or
  logs warnings; it applies validated positions. Overlap warns everywhere except contract v4 (R11). Tests:
  ArchitectPlanParser `invalid_dependencies_are_rejected` and `validator_rules_have_typed_codes`, PipelineDispatch
  `marker_plan_with_bad_parent_never_fans_out`, MissionHierarchy dispatch rejects a parentless `BranchStrategy`.
  SQLite affected suites: 723 tests, 0 failed, 6 skipped, then PipelineDispatch with the new case: 49 of 49.
- [x] **RS-4.5** One dispatcher. Delete `MissionService.DispatchPendingMissionsAsync` (`MissionService.cs:2109`), the
  head-of-queue synchronous copy. The Admiral dispatcher runs on a coalescing wake signal raised by mission create,
  completion, review, cancel, captain freed, endpoint breaker closed, and Harbor connect; the 10 s heartbeat stays as
  a safety net. Launches always run in the background with a shutdown-aware token (not `CancellationToken.None`,
  `MissionService.cs:792`). Evidence: `DispatchWakeSignal` (a SemaphoreSlim(0,1) that coalesces); `AdmiralService.RequestDispatch`, `StartDispatcher` and
  `DispatchOnceAsync`, with every pass serialized by `_DispatchGate` (completion paths never take it). Wakes come from
  `MissionService.DispatchRequested` (completion, review), `MissionTransitions.DispatchWake` (every status change except
  Assigned and InProgress, which covers cancel), `VoyageCreationService.DispatchRequested` (a voyage left Pending),
  `HarborConnectionManager.OnLinked`, and a timer when an endpoint breaker opens. `MissionService.ShutdownToken`
  replaces `CancellationToken.None` for background launches. Captain creation is left to the heartbeat. Tests:
  SequentialDispatch `dispatch_wake_coalesces`, `completion_wakes_dispatcher_without_waiting`,
  `background_launch_stops_on_shutdown`; 11 cases that relied on inline re-dispatch now assert the wake and run one pass
  (`DispatchWakeProbe`). The fan-out case now allows the cloned worker to start in the same pass (one pass fills every
  free captain). SQLite affected suites: 813 tests, 0 failed, 6 skipped.
- [x] **RS-4.6** Admission pipeline. Ordered gates (readiness, dependency, barrier, global capacity, persona routing,
  captain eligibility, runtime and endpoint capacity, usage selection, placement), each returning a typed
  `MissionAssignmentBlocker`. Dispatch, `DescribePendingReasonAsync`, `_PlacementWaitReasons`, and
  `AdmissionStuckMonitor` all read that one result. The stuck monitor stops re-running admission and only watches
  blocker age. Evidence: every exit of `TryAssignAsync` records one typed blocker (`NoteWait`/`NoteUnadmitted`);
  the wrapper stores it as `MissionAdmissionResult` (`IMissionService.GetAdmissionResult`, with `FirstObservedUtc`)
  after the locks are released. The captain gate is one function (`CaptainGateBlocker`) used by dispatch and by
  `GetAssignmentBlockerAsync`, which only adds per-captain detail. `DescribePendingReasonAsync`,
  `_PlacementWaitReasons`/`GetPlacementWaitReason`, `IsAdmissionReadyForMonitoringAsync`, and
  `SchedulerAllowsAdmissionObservationAsync` are deleted. `AdmissionStuckMonitor.ObserveAsync` reads the results of
  each heartbeat pass (AwaitingDispatch with every gate open, three passes, same version) and reads the database only
  to confirm a flagged mission; the event payload adds `BlockedSinceUtc` and `LastResult`. Tests:
  AdmissionStuck rewritten (`dispatch_records_the_blocker_reason_*`, reset cases, dedupe), parity assertions added
  to CaptainAdmissionParity and MissionAssignmentBlocker. SQLite affected suites: 858 tests, 0 failed (after
  switching one reflection call to `DispatchOnceAsync`), 6 skipped.
- [x] **RS-4.7** Admission snapshot. One read per pass of captains, active missions grouped by vessel, endpoints,
  voyages, overrides, and personas; every pending mission is evaluated against it in memory; only the atomic claim
  re-reads. Add projection queries (`WHERE vessel_id = ... AND status IN (...)`, no description column) on all four
  providers. Evidence: `DispatchPassCache` held per pass by `IMissionService.BeginDispatchPass` (an
  AsyncLocal scope the Admiral opens around each pass; vessels are cached by the Admiral). Cached: captains, execution
  slot count, running missions and admission rows per vessel, voyages (and their overrides), vessels, personas,
  endpoints, model profiles. A claim attempt, a mission leaving Pending, an error or a transient retry drops captains,
  slots, running missions, admission rows and voyages (`PassStateChanged`); background launches clear the scope.
  Projection `IMissionMethods.EnumerateRunningOnVesselAsync` (vessel and status IN Assigned, InProgress, Verifying,
  index `idx_missions_vessel_status`) on all four providers replaces three whole-status scans per mission. Deviations:
  the projection keeps `description` because broad-scope detection reads it (outputs and diffs are excluded), and
  each attempt still re-reads its own mission row by primary key, because the assignment write saves the whole row
  and a pass-start copy would overwrite a concurrent edit. Tests: Services.AdmissionSnapshot
  (`running_on_vessel_returns_only_running_missions_of_that_vessel`, `a_pass_reads_shared_state_once_for_every_pending_mission`,
  `a_claim_in_the_pass_is_seen_by_the_next_mission`, which fails with the post-claim refresh removed). SQLite affected
  suites: 880 tests, 0 failed, 6 skipped.
- [x] **RS-4.8** One capacity model per (captain, runtime, endpoint), computed from the snapshot, replacing the three
  calculations in `HasAvailableCapacityAsync` (`AdmiralService.cs:2654`), `SchedulerAllowsAdmissionObservationAsync`
  (`:2638`), and `EvaluateGlobalAdmissionAsync` (`CaptainRouting.cs:435`). Skip a mission early when every eligible
  captain is limited. Evidence: `CapacityModel` (global limit from `CountExecutionSlotsAsync`, runtime and endpoint
  occupancy from one captain snapshot, endpoint budget keyed by normalized base URL) is built once per pass state
  (`PassCapacityAsync`, dropped with the claim-sensitive cache). `EvaluateGlobalAdmissionAsync`, the runtime and
  endpoint caps in `ApplyCapacityLimitsAsync`, and the Admiral's `HasAvailableCapacityAsync` (through
  `IMissionService.ReadDispatchCapacityAsync`) all read it; the Admiral's own Working-captain count is gone, and its
  order (global limit, memory gate, captain room) is kept. `SchedulerAllowsAdmissionObservationAsync` no longer
  existed. Early skip: the Admiral pass stops when no captain has room under any cap (previously it stopped only on
  the global limit or no captain with a free slot, and tried each mission against full runtime or endpoint caps), and
  a mission skips the shared lock when the global limit is full. Per-mission early skip on eligible captains is not
  added: the eligible set depends on routing and the existing blocker detail would change. Also fixes an RS-4.7 gap:
  a claim outside the pass (voyage creation, verification repair) bumps a claim generation, and a pass that sees a
  newer one under `_CapacityLock` drops its claim-sensitive reads (`PassRevalidateUnderLock`). Endpoint availability
  (disabled, unhealthy, open circuit) stays outside the model. Tests: Services.CapacityModel
  (`the_dispatch_check_counts_the_global_limit_like_admission`, `every_captain_at_its_runtime_cap_leaves_no_dispatch_capacity`,
  `captains_sharing_an_endpoint_share_its_cap`, `a_full_global_limit_skips_the_mission_before_the_shared_lock`,
  `a_claim_outside_the_pass_is_counted_under_the_lock`, which fails with the under-lock revalidation removed).
  SQLite affected suites: 893 tests, 0 failed, 6 skipped.
- [x] **RS-4.9** Narrow `_CapacityLock` (`MissionService.cs:135`): select and place before the lock, then claim with
  a conditional write and re-validate. The lock covers only the counter check and claim. Concurrency test with
  parallel dispatch passes proving no over-admission. Evidence: preferred-captain resolution, captain selection, usage
  evidence, `CanAssignMissionAsync` and dock placement moved ahead of `_CapacityLock`. Under the lock,
  `PassRevalidateUnderLock` then `CapacityBlockerUnderLockAsync` re-check the global, runtime and endpoint caps for the
  chosen captain (the claim write enforces only the captain's own slots), then the existing conditional claim and the
  Assigned transition run. Vessel-then-capacity lock order and the transient-error (deadlock) retries are unchanged. A
  pass that loses its captain to a concurrent claim waits for the next cycle, as before. Tests: Services.CapacityModel
  (`parallel_passes_never_admit_past_the_global_limit`, `parallel_passes_never_admit_past_a_runtime_or_endpoint_cap`,
  where every pass chooses before any claims; both over-admit, 6 of 2 and 3 of 1, with the re-check removed;
  `a_slow_placement_does_not_hold_the_capacity_lock`). SQLite affected suites: 896 tests, 0 failed, 6 skipped.
- [x] **RS-4.10** Launch intent. Persist `LaunchIntentUtc` and the job or process id as part of launch. On startup,
  find and stop any orphaned agent process for a dock (by job id, worktree path, or command line) before resuming in
  that dock. Crash-point test between launch and PID record. Includes the `Services.CrashConsistency` case for
  `CrashPointNames.LaunchBeforeProcessRecorded` (one live agent per mission after restart). Evidence:
  `TryAssignAsync` writes `LaunchIntentUtc` (conditional on Assigned) before `OnLaunchAgent` and clears it when the
  launch is recorded, rolled back or failed. The local launch handler already saved the process id and exact start
  time (`SetProcessLaunchAsync`) inside `OnLaunchAgent`, before the crash point; `CleanupStaleCaptainsAsync` now stops
  that process through `ProcessSupervisor.StopIfVerified` (start time must match, so a reused id is never touched)
  and sends a mission with a recorded intent down the interrupted path (resume in its dock) instead of "never
  launched". Harbor launches need no new field or capability flag: the existing `HarborHandshakeAck.OwnedJobIds`
  makes a reconnecting Harbor stop every job the restarted Admiral does not own, and `HarborProcessLedger` covers a
  Harbor restart, so `LaunchJobId` stays unused. Not covered: a crash in the instant between process start and the
  start-time write inside the handler (no worktree or command-line scan was added). New case
  `launch_crash_before_process_recorded_leaves_one_agent` starts a real process as the agent; with the stop removed
  it fails ("the agent left running by the crashed Admiral was stopped"). SQLite affected suites: 904, 0 failed.

## Phase 5: Failure classification, recovery, and escalation

- [x] **RS-5.1** One failure taxonomy. Keep `MissionFailureKindEnum` as the single kind on a mission; every source
  (runtime, landing, merge queue, definition of done, BlackBox, FailureCause rules) maps into it in exactly one
  class, `FailureKindMapper`. Remove the inline ternary near `AdmiralService.cs:3013` and the hard-coded `Infra` in
  `LandingEntryAsync` and `FlagMissingMergeHandoffsAsync`. Document the mapping table in `docs/MODEL_HETEROGENEITY.md`
  Part 4. Evidence: `FailureKindMapper` replaces `MissionFailureClassifier`, `LandingFailureClassifier` and
  `MergeQueueFailureClassifier`; `HandleTerminalProcessExitFailureAsync` and its three callers map through
  `FromRuntimeExit` (the negative exit code rule moved into it), and `FlagMissingMergeHandoffsAsync` uses
  `FromLostLandingHandoff`. `LandingEntryAsync` no longer exists. BlackBox and FailureCause rules only read the kind.
  Table added to docs/MODEL_HETEROGENEITY.md Part 4. `Services.FailureKindMapper` (renamed from
  `Services.MissionFailureClassifier`, new `runtime_exits_map` case), `Services.LandingConflict` and
  `Services.MergeQueueProcessing` pass.
- [x] **RS-5.2** `Unclassified` runtime failure kind. `RuntimeFailureClassifier.Classify` returns it instead of
  `Crash` when no provider error is parsed or the status is unmapped (`RuntimeFailureClassifier.cs:48`). Recovery
  gives `Unclassified` one resume, then escalation. `Crash` is reserved for a process that died abnormally. Count
  `Unclassified` per runtime and model (G6). Evidence: `RuntimeFailureKindEnum.Unclassified`;
  `RuntimeFailureClassifier.IsAbnormalExit` (exit 129 to 192 or negative) keeps `Crash`, everything else unexplained is
  `Unclassified`. `HandleProcessExitAsync` gives it `_UnclassifiedResumeLimit` (1) resume, then `TryEscalateToPlannerAsync`;
  it counts toward the crash loop. `FailureKindMapper.FromRuntimeExit` maps it to `Crash` so the bounded rescue still
  applies until RS-5.6. G6 counts it with a runtime / model breakdown. New case
  `handle_process_exit_async_resumes_unclassified_exit_once`; affected suites pass (RuntimeFailureClassifier,
  RuntimeProviderErrorParser, AdmiralService, AgentLifecycleHandler, GateScoring, BlackBox*, StageOutcomes, runtimes).
- [x] **RS-5.3** Open-model error patterns in `RuntimeProviderErrorParser`: Ollama, vLLM, LM Studio, and LiteLLM
  messages for runner terminated, out of memory, model not found, model loading, context overflow, and tool-call
  parse failures. 400s on tool or schema errors map to `TaskFailure`. Table tests from captured real outputs
  (store them as fixtures). Evidence: `OpenModelErrorPatterns.Match` (the one place an open-model message is read) feeds
  `RuntimeProviderErrorParser.FromHttpStatus`, CLI stderr parsing, and `RuntimeFailureClassifier.Classify` for generic
  types (Mux `llm_error`). New types `runner_terminated`, `model_loading`, `unavailable_error` (Transient),
  `model_out_of_memory`, `tool_calling_unsupported` (ModelUnavailable), `tool_call_parse_error` (TaskFailure).
  Fixtures in `src/Test.Shared/Fixtures/OpenModelErrors/open-model-errors.json`: 4 rows captured 2026-11-25 (proxied
  Ollama, Ollama Cloud; hosts removed), 19 from server source text (vLLM, LM Studio, llama.cpp, LiteLLM had no live
  server). `open_model_errors_classify_from_fixtures` and `open_model_patterns_ignore_unrelated_text` pass, as do
  RuntimeFailureClassifier, AdmiralService, runtime and OpenModelSimulator suites.
- [x] **RS-5.4** 429 handling. A 429 without `insufficient_quota`, a reset time, or a usage-limit error type is
  `Transient` and honors `Retry-After`; it does not quarantine the captain. 402 stays `UsageLimit`. Evidence:
  `RuntimeFailureClassifier` checks rate limits first (`IsRateLimit` / `IsExhaustedQuota`; a `Retry-After` over
  `MaxTransientRetryAfterSeconds` (600) also counts as exhausted). `ApiAgentRuntime` reads `Retry-After` from the last
  PolyPrompt `CallDetail` (`MaxCallDetails = 2`), waits up to 30 s in the run and otherwise returns it on the provider
  error; `TransientResumeDelay` honors it. Cases `rate_limit_is_transient_unless_quota_is_gone` and the simulator's
  `retry_after_is_honored` (real HTTP 429); 375 cases across the classifier, parser, Admiral, runtime, Mux, handoff and
  usage-enforcement suites pass. Docs: CAPTAIN_ROUTING.md. Also fixes a MuxRunOutcome case RS-5.2 left failing.
- [x] **RS-5.5** Recovery policy per model profile (or tier default): in-place resumes, transient grace, Judge rework
  rounds (replacing the hard-coded `_MaxJudgeReworkRounds = 2`, `MissionService.cs:2264`), format repairs, and
  "escalate after". Global settings become the defaults. Surfaces: REST, MCP, SDK, dashboard model profile page,
  TUI, mobile (read-only), docs. Evidence: `ModelProfile.Recovery` (`MaxResumes` 0-10, `UnclassifiedResumes` 0-3,
  `TransientGraceMinutes` 0-120, `JudgeReworkRounds` 1-5, `OutputRepairAttempts` 0-3; null uses the global setting),
  migration 135 on all four providers, and `RecoveryPolicyResolver` (Judge rounds take the larger value, so a profile
  never shortens the chain; Unclassified resumes are capped by the resumes). New settings `UnclassifiedResumeLimit`
  (1) and `JudgeReworkRounds` (2) replace the two constants. The Admiral resume path, output repair and Judge rework
  read the policy of the mission's captain; stall recovery keeps `MaxRecoveryAttempts`. REST PUT and GET (plus
  `EffectiveRecovery` on single reads), MCP `get_model_profile`, the .NET SDK (shared models), Postman, REST_API.md,
  MODEL_HETEROGENEITY.md, README settings, and the regenerated API surface. New suite `Services.RecoveryPolicy`, Admiral
  case `model_profile_recovery_policy_overrides_unclassified_resumes`, and round-trip assertions in
  `Database.ModelProfiles` (passes on SQLite, PostgreSQL, MySQL and SQL Server), `E2E.ModelProfiles` and
  `Client.Contract.ModelProfiles`. Not done here: there is no tier default (the global settings are the defaults);
  "escalate after" moves to RS-5.6, which adds the ladder it acts on; the dashboard, TUI and mobile have no model
  profile screen yet, so the editor lands with RS-12.12, RS-12.9 and RS-12.16.
- [x] **RS-5.6** Escalation ladder. Personas (and optionally pipelines) get an ordered escalation ladder of captain
  matchers, stored as relational child rows like `CaptainPriorities`. Escalation is allowed for standalone missions,
  can take more than one hop, and must move to the next rung or a strictly higher effective tier (G12). Triggers:
  exhausted resumes, `Unclassified` after its resume, Judge FAIL past the rework budget, repeated no-op, repeated
  malformed tool calls, and an open endpoint breaker (optional early fallback). Persists RS-3.1 columns. Surfaces:
  REST, MCP, SDK, Postman, dashboard persona and pipeline editors, TUI, mobile, docs. Evidence: `Persona.EscalationLadder` (the
  `PersonaCaptainPriority` matcher, child table `persona_escalation_rungs`, migration 136 with the profile column
  `recovery_escalate_after_resumes`). `CaptainEscalationSelector.ChooseAsync` returns an `EscalationChoice`: the first
  eligible captain on a rung above the failed captain's rung (idle first), none past the top rung, and with no ladder
  only a strictly higher effective tier. `MissionService.ChooseEscalationAsync` / `ApplyEscalationAsync` hold the
  guards (work stages only, `MaxEscalations` hops, default 2, strict assignments refused) and the in-place rewrite;
  standalone missions escalate. Triggers: spent resumes and `Unclassified` past its resume (Admiral), the profile's
  `EscalateAfterResumes` (task and Unclassified failures; transient outages keep resuming), two consecutive
  `tool_call_parse_error` exits, repeated no-op or unfinished completions (fresh re-dispatches for the new captain), Judge
  rejection past the budget (one escalated round per hop, in-place and rework hops share `MaxEscalations`), and the
  opt-in `EscalateOnOpenEndpointBreaker`. Surfaces: REST and MCP persona create/update, MCP `get_model_profile`, the
  .NET SDK (shared models), Postman, dashboard persona editors and detail, TUI persona form and overview, mobile
  persona detail and form, REST_API.md, CAPTAIN_ROUTING.md, MODEL_HETEROGENEITY.md, README settings, CHANGELOG, API
  surface. Tests: new `Services.EscalationLadder` (6), Admiral cases `repeated_malformed_tool_calls_escalate` and
  `escalate_after_resumes_skips_remaining_resumes`, `Services.NoOpCompletion` `repeated_noop_escalates_up_the_ladder`,
  `Services.RecoveryPolicy` (2 more), multi-hop and same-tier refusal in `Services.MultiRuntimeFlow`, ladder round trip
  in `Database.ScopedPaging` (passes on SQLite, PostgreSQL, MySQL and SQL Server), `E2E.PersonaPriorityApi`,
  `Tui.Personas`, dashboard and mobile persona tests (2026-11-25). Not done here: a pipeline-level ladder (optional in
  this item; persona ladders cover pipelines, whose stages resolve personas); the malformed-tool-call count is in memory
  and restarts with the Admiral; the iteration-limit trigger is RS-5.12; G12 is measured by the gate in RS-14.6.
- [x] **RS-5.7** Escalation carries memory: the escalated attempt's prompt gets the prior attempt's failure kind,
  output tail, diff summary, and BlackBox rule hint, rendered from typed records. Evidence: `EscalationMemory` (typed
  trigger plus recalled facts) filled by `MissionService.ApplyEscalationAsync` before the dock goes (output tail from
  the mission or the peeked runtime output, redacted, 2000 characters; non-scratch files changed since the dock start
  commit) and rendered by `EscalationMemoryRenderer` into the escalation note. The hint runs `BlackBoxAdvisor.Analyze`
  over an in-memory record (step, kinds, exit code, output tail) and keeps the strongest non-catch-all finding. All
  three triggers pass typed input: runtime exits (`BlackBoxSiteFacts.ExecutionStepOf`), stage-check failures
  (StageCheck) and no-op exhaustion (NoOp or Unfinished). The previous output is cleared on escalation. Tests:
  Services.EscalationLadder `apply_escalation_pins_and_records` (note content, redaction, bound, cleared output) and
  `escalation_memory_renders_hint_and_files`; EscalationLadder plus NoOpCompletion 30/30, AdmiralService plus
  MultiRuntimeFlow 109/109. Docs: CAPTAIN_ROUTING.md, CHANGELOG. Diff summary is the changed-file list, not line
  counts (the branch diff is not read on this path).
- [x] **RS-5.8** Rescue routing. From the second rescue on, `MissionRecoveryCoordinator.DispatchRescueAsync` uses
  the escalation selector with the failed captain excluded (`rescue.Tier = original.Tier`, line 316, is replaced).
  The rescue brief includes the output tail and BlackBox hint. Evidence: from the second rescue on, the rescue is
  pinned (`RequestedCaptainId`, `EscalatedFromCaptainId`, `EscalationCount = 1`) to
  `CaptainEscalationSelector.ChooseAsync` with the failed rescue's captain excluded; the original tier stays as a
  floor, and with no qualifying captain the rescue goes out unpinned. Both briefs get a "Last Failed Attempt" section
  built from `EscalationMemory` and `EscalationMemoryRenderer.RenderFacts` (kind, hint, redacted 2000-character
  tail). Case `second_rescue_excludes_failed_captain_and_carries_memory`; MissionRecoveryCoordinator, EscalationLadder
  and CrashConsistency 44/44, BlackBoxCaptureSites 86/86.
- [x] **RS-5.9** Fix the RecoveryExhausted notification: fire it when the rescue budget is actually exhausted
  (coordinator `ExhaustedNote`), not on every terminal failure keyed by captain (`AdmiralService.cs:3083`). Mitigated
  incidents close only after a verification cycle shows the fix held. Evidence: `MissionRecoveryCoordinator.Escalation`
  fires `RecoveryExhausted` once per incident, keyed by the original mission, behind the exhausted note; the Admiral's
  terminal-failure path fires `MissionFailed` keyed by mission. A Mitigated incident closes only when a rescue is still
  Complete on the next pass, and otherwise reopens with a "Verification failed" note. MissionRecoveryCoordinatorSuite
  `failed_rescue_redispatches_until_cap` (one notification) and `mitigation_reopens_when_rescue_regresses`;
  MissionRecoveryCoordinator, EscalationService and RecoveryState suites 30/30.
- [x] **RS-5.10** Collapse the recovery loops into one `RecoveryPolicyEngine` that decides resume, repair,
  escalate, rescue, or fail from (failure kind, attempt history, recovery policy). Resume-in-place, escalate to
  planner, Judge rework escalation (`MissionService.Rework.cs:69`), and the incident rescue loop become actions it
  chooses. Evidence: `RecoveryPolicyEngine.Decide(RecoveryFacts, RecoveryPolicy)` returns a `RecoveryDecision`, an
  ordered list of `RecoveryStep`s (`RecoveryActionEnum`: Redispatch, Resume, Repair, Rework, Escalate, Rescue, Wait,
  Fail) for six sites (`RecoverySiteEnum`: RuntimeExit, ProcessLost, NoOpCompletion, OutputContract, JudgeRejection,
  Incident). Callers gather facts and carry out the first step they can, falling through when one cannot run (no
  stronger captain). The Admiral exit path (`DecideRuntimeExitAsync`; interruption, breaker and malformed-tool-call
  escalation, resume, escalate, fail), the health monitor's lost process, no-op completion, output repair, Judge rework
  (trigger and limit texts now built by the engine) and the coordinator's rescue loop (`DecideRescue`, both the first
  and later rescues) all use it; the executors (resume, escalation selector, rescue dispatch) are unchanged. Behavior is
  unchanged except a successful escalation resets the malformed tool call count. "Escalate to planner" is the escalation
  selector's no-ladder order, which the Escalate action reaches. New suite `Services.RecoveryPolicyEngine` (12 cases);
  RecoveryPolicyEngine, RecoveryPolicy, EscalationLadder, NoOpCompletion and MissionRecoveryCoordinator 74/74;
  AdmiralService, MultiRuntimeFlow, PipelineDispatch, ContractRepair and CaptainRouting 219/219. Docs: CAPTAIN_ROUTING.md
  "How recovery is chosen", CHANGELOG. No REST, MCP, settings or SDK change.
- [x] **RS-5.11** Typed exceptions instead of message matching: `MergeQueueEntryNotFoundException` for
  `ex.Message.Contains("merge queue entry")` (`MissionService.Completion.cs:954`), a typed git result for
  `"not something we can merge"` (`MergeQueueService.cs:927`), and process-start detection without
  `"trying to start process"` (`LandingFailureClassifier.cs:49`). Evidence: the handoff check throws
  `MergeQueueEntryNotFoundException` (an `InvalidOperationException`, so other handlers are unchanged) and the
  MissingMergeHandoff step tests its type. The Harbor merge fallback now runs when the merge fails, the local branch
  does not resolve and `origin/<branch>` does (two `rev-parse --verify` calls, only on failure). `FailureKindMapper.IsProcessStartFailure`
  drops the message clause: it only fed `FromLandingException`, whose fallthrough is Infra anyway, so no outcome
  changes. Tests: LandingConflict, MergeQueue, MergeQueueProcessing, LandingPipeline 87/87. Remaining message matches
  (DB unique violations, git identity, quota, Harbor "not installed") are outside this item; RS-11.4 tracks them.

- [x] **RS-5.12** Iteration-limit exhaustion (gpt-oss:120b hit the limit 4 times, HDR) becomes a typed failure kind
  and an escalation trigger in RS-5.6. Evidence: `RuntimeFailureClassifier.IsIterationLimit` recognizes the stop from
  the `max_iterations_reached` error type (Mux and the API endpoint runtime report it), never the message; the runtime
  failure kind stays TaskFailure, so REST, MCP, SDKs and the dashboard see no new enum value. The Admiral counts
  consecutive iteration-limit exits per mission (in memory, like malformed tool calls; reset on a clean exit, a
  different failure or an escalation) and sets `RecoveryFacts.RepeatedIterationLimit` on the second, which makes
  `RecoveryPolicyEngine` escalate before resuming. A first stop still resumes. Claude Code's `error_max_turns` is not
  mapped to a provider error today, so it is not covered. Tests: `Services.RecoveryPolicyEngine`
  `repeated_iteration_limit_escalates_first`, `Services.AdmiralService` `repeated_iteration_limit_escalates`;
  RecoveryPolicyEngine, AdmiralService, EscalationLadder, RuntimeFailureClassifier 111/111. Docs: CAPTAIN_ROUTING.md,
  MODEL_HETEROGENEITY.md, REST_API.md, CHANGELOG.

## Phase 6: Output contracts and repair

The biggest single cost for open models today is that a formatting slip reruns the whole stage. This phase makes
the contract a schema, lets the model submit through a tool, and repairs format problems without redoing the work.

- [x] **RS-6.1** One contract source. Generate the Architect plan, Judge verdict, Worker result, Test Engineer, and
  Verifier contracts (prompt text, JSON example, field table, accepted verdicts) from the C# models and
  `JudgeVerdictEnum`. Remove the copies in `PromptTemplateService.cs:1065-1080` and `:1116-1181`,
  `JudgeContract.OutputContract`/`PersonaPromptFallback`/`RoleSummary`, `PersonaPromptVariants`,
  `MissionService.Instructions.cs:836`, and `ContractRepair.CanonicalVerdictLine`. The Architect prompt shows one
  versioned contract (v4) with a complete JSON example. `RoleSummary` loses its .NET-specific advice (move it to the
  .NET workflow profile). Test that every persona prompt contains exactly one contract and that it matches the
  parser. Evidence: `StageOutputContract` generates all contracts; `Services.StageOutputContract` checks the Architect
  example parses as a valid v4 plan, every verdict line parses, and every result contract ends with the completion line;
  `PromptVariants.every_persona_instructions_state_one_contract`. Untouched templates upgrade via briefs v7 and variant
  snapshots; edited templates are kept. 18 affected suites 357/357. Rework and rescue briefs still repeat the completion
  sentence (RS-6.11).
- [x] **RS-6.2** Submit tools. MCP tools `armada_submit_plan`, `armada_submit_verdict`, and `armada_submit_result`
  with `inputSchema` generated from the models, validated synchronously, errors returned in the same call so the
  model fixes them in-loop. The validated submission is stored as a typed record on the mission attempt; parsers stop
  re-reading `AgentOutput` (`MissionService.Handoff.cs:133-153` parses it several times). Marker text remains the
  fallback for runtimes without tools. Surfaces: MCP, docs/MCP_API.md. Evidence: e7f214e12. Schemas from `StageSubmissionSchema`,
  `StageSubmissionValidator`, `stage_submissions` (migration 138, four providers), `StageSubmissionText.EffectiveOutput`
  at every decision site; the plan is parsed once into the fan-out. A valid submission wins over markers (logged). Tests:
  `Services.StageSubmission` (12); providers 16/16 x3.
- [x] **RS-6.3** The same submit tools are native tools in `ApiAgentRuntime`. When the model stops calling work
  tools without submitting, send `tool_choice` set to the submit tool (PolyPrompt `ToolChatRequest.ToolChoice`);
  where the endpoint supports `response_format: json_schema`, use it for the final turn as a second fallback.
  Evidence: e7f214e12. `NativeSubmitTool` (forwarded to MCP or written as a block); forced turns offer only
  the submit tool with `ToolChoice = "required"` (PolyPrompt 3.5.0 cannot name a tool), only when the text also fails.
  `response_format: json_schema` not added: PolyPrompt 3.5.0 has no response-format option. Tests:
  `native_submit_verdict_fixes_schema_error_in_loop`, `stopping_without_submit_forces_the_submit_tool`.
- [x] **RS-6.4** Format-only repair turn. Replace "Do the same job again" in `TryScheduleOutputRepairAsync`
  (`MissionService.cs:1395`) for format-only failures: (1) in-session restatement when the runtime supports resume
  (ApiEndpoint always; Claude Code and Codex via session resume), (2) otherwise a tool-less format call that gets the
  stored output, the contract, and the parser's error list, optionally routed to a configured normalizer captain
  (for example Opus). The repair keeps the original output and diff, is bounded (RS-5.5), never creates a PASS,
  findings, or evidence the original lacked (MODEL_HETEROGENEITY principle 4), and records its outcome (RS-1.4). A
  full rerun is the last resort. Evidence: f0a1b86f0. `FormatRepair` and `MissionService.FormatRepair`: one tool-less call through the
  probe executor (or `OutputRepairNormalizerCaptainId`), accepted only with no invented verdict, PASS, file, check,
  scenario or mission, recorded as `ContractRepairKind.FormatRepairTurn`, then the full rerun. No runtime keeps a
  session after exit, so there is no in-session restatement yet; recorded as a repair kind instead of a new
  `StageDispositionEnum` value; the format turn does not consume `OutputRepairAttempts` (at most one per attempt).
  Tests: `FormatRepair` (7), `MultiRuntimeFlow.judge_format_turn_restates_without_rerun`, `judge_format_turn_cannot_invent_pass`,
  `gated_judge_gets_no_format_turn`, `PipelineDispatch.architect_plan_type_error_gets_format_turn`.
- [x] **RS-6.5** Plan parser tolerance (`ArchitectPlanParser.cs`, `ContractRepair.TryRecoverArchitectPlan`): reuse
  `Protocol/EmbeddedJsonExtractor` for bare JSON, `~~~` fences, and prose in fences, preferring the last valid
  candidate; `NumberHandling = AllowReadingFromString`; case-insensitive `kind` and `branchStrategy`; keep the
  `JsonException` path, line, and position; return errors as a capped, ordered list; replace the null-error "no block
  found" signal with a result enum. Evidence: dec336d5f. `ArchitectPlanParser.Parse` returns a status enum and up to 20
  ordered errors with JSON path, line and position; tilde fences parse; one bare or prose-in-fence plan is recovered and
  two are refused (R6); no type or case coercion (R6).
- [x] **RS-6.6** Verdict and marker tolerance. `ContractRepair` recovers prose verdicts (`**Verdict: NEEDS_REVISION**`
  under `## Verdict`) without requiring the token `ARMADA`; when two canonical lines conflict, accept the final
  non-empty line outside fences only if it is the last line; reset fence state at the `[...output truncated...]`
  marker. `ApiAgentRuntime`'s completion check (`ApiAgentRuntime.cs:1048`) uses the same decoration-tolerant matcher
  as Core and takes `requiresCompletion` explicitly instead of sniffing the prompt. Prompts stop showing markers in
  backticks. Evidence: dec336d5f. Prose FAIL and NEEDS_REVISION are recovered, prose PASS is not (PipelineDispatch
  requires a protocol line for a landing PASS); no last-line rule (R5); fence state resets at the truncation marker;
  `ARMADA_REQUIRES_COMPLETION_LINE` and a shared `CompletionLine` matcher. ModelOutputContract 27/27, none skipped.
  Backtick removal from prompts lands with RS-6.1.
- [x] **RS-6.7** Structured findings. The Judge and Test Engineer submit findings as
  `{file, line, severity, scenario, requiredFix}` through RS-6.2; the Judge template asks for a `## Findings`
  section for text runtimes; the eight-regex heading scraper in `HandoffArtifactBuilder.ExtractFindings` becomes the
  fallback. Evidence: e7f214e12. `StageFinding` mapped by `StageSubmissionText.HandoffFindings`; `ExtractFindings` is the
  fallback; contracts ask for `## Findings`. Test: `structured_findings_replace_the_scraper`.
- [x] **RS-6.8** PASS evidence. A PASS must cite the checks it ran or at least one inspected file (deferred R19 in
  `archive/PROMPT_AND_HANDOFF_IMPROVEMENTS.md`). A PASS without it becomes a format-only failure. Evidence: f0a1b86f0.
  `JudgeEvidence.HasPassEvidence` (a file path or a named check) in `JudgeContract.ValidatePass` and the generated
  contract. Tests: `FormatRepair.pass_without_evidence_is_rejected`, `MultiRuntimeFlow.judge_pass_without_evidence_reruns`.
- [x] **RS-6.9** One handoff store. Every stage reads handoffs from `HandoffArtifact` rows created once, at stage
  completion, in the completion transaction. Remove the lazy `GetOrCreateForStageAsync` at launch and the unstored
  build in `MissionPromptBuilder.BuildTemplateParams`; replace the length-based `IsSameAttempt` with an attempt id.
  Evidence: fbe9cf638. Migration 137 adds `attempt_id`; artifacts are written in the handoff and fan-out batches
  and before the rework chain; lazy build at launch and length dedup removed; pre-137 stages render an unstored legacy
  handoff. Tests: `outcome_links_handed_on_artifact`, `launch_reads_without_storing`,
  `handoff_batch_writes_artifact_atomically`; providers verified on Postgres, MySQL, SQL Server (47/47 each).
- [x] **RS-6.10** Chain-aware handoff. A downstream stage sees the artifacts of the whole chain, newest first,
  within its budget (the Judge sees the Worker's report, not only the Test Engineer's). Evidence: fbe9cf638.
  `HandoffRenderer.RenderChainWithBudget`, `ReadEarlierChainAsync` (8 stages, same voyage). Test: `judge_sees_whole_chain`.
- [x] **RS-6.11** One prompt composer for first runs, rework (`ReworkPromptBuilder.Compose`), repair, and escalation:
  declared template slots instead of `personaPrompt.Contains(handoff.Evidence.Trim())`
  (`MissionService.Instructions.cs:115-131`), one render of the persona prompt, and a token budget for the whole
  instructions file (playbooks, skills, papercuts, persona prompt, contract, handoff). Remove the unused
  `MaxMissionDescriptionChars` and `MaxPersonaSummaryChars` or wire them into the budget. Evidence: be14a9980.
  `InstructionsComposer` (declared sections, trim ranks, budget = context/4 minus `PersonaToolScope.EstimateSchemaTokens`,
  floor 2048, no trim when context is unknown); one persona render; slot-based `DeliverHandoff`; contract once at the
  file end. Unused private constants removed. Tests: `Services.InstructionsComposer` (5),
  `PromptVariants.every_launch_kind_states_one_contract`, `small_context_trims_in_order_and_keeps_contract`,
  `ReworkPrompt.legacy_section_contract_removed`; 14 suites 348/348.
- [x] **RS-6.12** Deprecate the legacy `[ARMADA:MISSION]` regex plan path (`MissionService.ArchitectPlan.cs:310-625`):
  keep it behind a setting defaulting on in 1.5.0, log a deprecation warning when it is used, and list it in
  `DEPRECATION.md` for removal in a later release. Precompile any regex that remains. Evidence: `AcceptLegacyMissionMarkers`
  (default true) gates the marker path, each use logs a D-027 warning, and the regexes are precompiled; DEPRECATION.md
  D-027 (removal 2.0). Tests: `PipelineDispatch.architect_legacy_markers_rejected_when_disabled` and the warning
  assertion in the fan-out case; PipelineDispatch, Docs.Deprecation, Tui.Parity, VsCode.Parity, BlackBoxConfiguration
  76/76.

- [x] **RS-6.13** Count tool schemas in every prompt budget (RS-6.11, RS-8.4) and scope the tool catalog per persona:
  qwen3.8 used 36,271 input tokens with the full 175-tool catalog against 723 with scoped tools (HER:1224).
  Evidence: 12b23bd7c. `PersonaToolScope` declares per-persona lists sent as `ARMADA_MCP_TOOL_SCOPE` to
  ApiEndpoint launches; the rest is deferred to discovery; `ApiContextCompactor.EstimateToolTokens` counts schemas in
  compaction pressure. Real catalog 273 tools / ~62,518 tokens vs Judge 5 / ~835. Tests: `judge_launch_exposes_persona_scope`,
  `persona_scope_applies_to_small_catalogs`, `tool_schema_estimate_counts_schemas`, `persona_tool_scopes_name_real_tools`.
  Operator override of the scope deferred to RS-8.4 (needs a Persona column across providers).

## Phase 7: API runtime hardening for open models

`docs/MODEL_GUIDANCE.md:67` says tool-calling reliability on generic runtimes "is the model's responsibility". After
this phase it is Armada's job to absorb the predictable failures.

- [x] **RS-7.1** Upgrade PolyPrompt from 3.5.0 to the current 5.x in every project that references it. Run the model
  provider simulation suites (`Services.ModelEndpoint`, `Runtimes.ApiAgent`) and the reasoning-continuation
  regression in `postman/README.md`. Evidence: (07f4e06c8) PolyPrompt 5.0.1 in Armada.Core and Armada.Server; one `ProviderClientBase` per
  endpoint; OpenAI stays on Chat Completions via `OpenAiCompatibleClient`; Azure on v1 Chat Completions (`ApiVersion`
  ignored, form field removal in Phase 12); Anthropic/Gemini/Voyage get a placeholder key under Armada auth, stripped by
  `ModelEndpointAuthHandler`. ModelEndpointService 43/43 and ApiAgentRuntime 49/49 against the local postman fixture
  (reasoning-continuation regression included); ModelEndpointAuth 8/8. 5.x still lacks json_schema and text tool-call
  recovery.
- [x] **RS-7.2** Text tool-call recovery. When a turn has no `ToolCalls`, parse `<tool_call>{json}</tool_call>`,
  `<function=name>...</function>`, and fenced JSON tool-call blocks (at the terminal branch near
  `ApiAgentRuntime.cs:693`, or in PolyPrompt if it belongs there) and execute them as real calls. Count recoveries
  per model. Evidence: (44f78592a) `TextToolCallParser` (Hermes `<tool_call>`, `<function=name>` with JSON or
  `<parameter=x>`, fenced JSON) in `ApiTurnLoop`, permitted tools only, never from an unsuccessful or length-cut reply;
  counted in `ApiTurnLoopResult.TextToolCallRecoveries` and `[recovery]` diagnostics per turn and per model (not
  stored). Switch `ApiRuntimeBehaviors.TextToolCallRecovery`; flag `api-text-tool-call-recovery-v1`. Suite
  `Runtimes.TextToolCallParser` (13), ApiTurnLoop and OpenModelSimulator recovery runs.
- [x] **RS-7.3** Malformed-tool-call 500. In `IsRetryableInferenceStatus`/`WithInferenceRetryAsync`
  (`ApiAgentRuntime.cs:456-460`), when the 500 body names a tool or JSON parse failure, do not resend the identical
  request: add a corrective user turn with the valid tool names and a schema hint, and vary the seed or temperature
  within the profile's bounds. Evidence: (5e634c424) matching failures return to `ApiTurnLoop`, which adds a `MalformedCallCorrection` turn
  and raises temperature 0.2 per attempt (cap 1.0; PolyPrompt 5 has no seed), at most
  `ApiRuntimeBehaviors.MalformedCallRetries` (default 2); then the existing classification. Harbor flag
  `api-malformed-call-retry-v1`. Streamed 429 Retry-After via `UsageHeaders.LatestObservation`. Tests
  `malformed_call_gets_corrective_turn`, `repeat_loop_gets_no_repeat_turn`, `malformed_call_retries_are_bounded`,
  `api_runtime_corrects_degenerate_generation`, `retry_after_is_honored_when_streaming`. Plain chat (no tools) unchanged.
- [x] **RS-7.4** Unknown tool name returns "unknown tool <name>; available: ..." instead of "disabled by the
  endpoint's inference policy" (`ApiAgentRuntime.cs:727`). Evidence: (44f78592a) `unknown_tool` lists up to 40 available tools; `tool_not_allowed` kept for
  policy-disabled tools. Tests `unknown_tool_lists_available`, `unknown_tool_list_is_capped`.
- [x] **RS-7.5** Repetition guard: N identical consecutive tool calls (setting, default 3) inject a corrective note;
  2N end the run as a classified `TaskFailure` (feeds escalation). Evidence: (44f78592a) inference option `repeatedToolCallLimit` (default 3, 2 to 20) on REST, dashboard, TUI,
  mobile; note appended to results from call N, 2N ends as `degenerate_output` (TaskFailure). Flag
  `api-repetition-guard-v1`. Tests `repeated_calls_get_note`, `repeated_calls_stop_run`,
  `different_arguments_reset_count`. Merged head: nine affected suites 195/195.
- [x] **RS-7.6** Reasoning hygiene: strip reasoning when only `</think>` is present, and strip reasoning from history
  before it is sent back (`ToAssistantMessage()` near line 691), except where the provider requires reasoning replay
  (keep the PolyPrompt 3.4.0 regression passing). Evidence: (5e634c424) `ApiTerminalPolicy.VisibleText` uses `ReasoningText.Strip`; history text cleaned in
  `ApiTurnLoop`, `ProviderState` untouched (ApiAgentRuntime passes with the postman fixture). Harbor flag
  `api-reasoning-hygiene-v1`. Tests `history_reasoning_is_stripped`, `bare_close_think_is_stripped`.
- [x] **RS-7.7** Streaming on by default for OpenAI-compatible and Ollama endpoints (`InferenceOptions.Streaming`),
  so progress heartbeats reach the stall detector. Evidence: (75bbb3a20) `InferenceStreamingModeEnum` and `InferenceOptions.StreamingMode`; Automatic streams
  OpenAI-compatible, Ollama and Ollama Cloud in process and, through `RemoteAgentRuntime`, on Harbors advertising
  `api-inference-streaming-v1`; explicit On/Off win; stored 1.4.0 `false` reads as Automatic. Forms on dashboard, TUI,
  mobile. Tests `streaming_mode_defaults_and_compatibility`, `harbor_launch_resolves_automatic_streaming`. Open: a 429
  Retry-After on the streaming path (checked in RS-7.3).
- [x] **RS-7.8** Context discovery. When `ContextLength` is null, discover it at endpoint validation and on first use
  (PolyPrompt model info, Ollama `/api/ps`, vLLM `/v1/models` `max_model_len`), store it on the model profile as
  "discovered", and let a configured value override it. Compaction is always on with a known window. Evidence: (045d128e4) discovery at validation and before the first API turn (cached 10 min); stored
  as `DiscoveredContextLength`/`DiscoveredContextSource`/`ContextDiscoveredUtc` (migration 139, all four providers);
  configured value wins; trained maximum never used; Harbor discovery behind `api-context-discovery-v1`; profiles record
  `ServedContextSource`. A new window yields a new profile (served context is in the identity key) rather than a
  revision. Suite `Services.ModelContextDiscovery` (11) and `discovered_window_applies_without_configured`; DB suites on
  Postgres, MySQL, SQL Server.
- [x] **RS-7.9** Tool output limits scale to the window: read and process output caps default to a percentage of the
  window (setting, default 12%) with paged reads, replacing the fixed 1 MB read and 100 KB process caps.
  `ToolSafetyLimits` statics become settings on the runtime options. Evidence: (c23d311c7) `ToolSafetyLimits` statics became `ToolLimits`, set per run on the registry,
  `ReadFileTool` and `RunProcessTool`; hard maximums are `toolOutputWindowPercent` (default 12) of a known window at 3.2
  characters per token (floor 24,000 characters read, 8 KB per stream, ceiling 1 MB); paged explicit reads; option on
  REST, dashboard, TUI, mobile. Suite `Runtimes.ToolLimits`.
- [x] **RS-7.10** Compaction shrinks old assistant tool-call arguments (file bodies written by `write_file`) and
  reasoning, not only tool results. Token estimation uses a tokenizer when the profile names one and about 3.2
  characters per token for code otherwise. Evidence: (ff5e295e7) `ApiContextCompactor.Compact` replaces old write/edit arguments with valid
  JSON keeping the path and a size note and strips old reasoning (`ProviderState` and thought signatures untouched);
  same recency rule as tool outputs. `CharacterRatioTokenEstimator` (3.2) shared with `ToolLimits` and
  `InstructionsComposer`; `ITokenEstimator`/`TokenEstimators` seam for a named tokenizer (no profile field yet). Schema
  constants recalibrated (built-in 3000, per MCP tool 200). Suite `Runtimes.ApiContextCompactor` (6); nine affected
  suites 100/100.
- [x] **RS-7.11** Extract `ApiAgentRuntime.RunLoopAsync` into a turn loop, terminal-handling policy, and context
  policy classes with unit tests per class. Evidence: (7ffd9ed2e) `ApiTurnLoop`, `ApiTerminalPolicy`, `ApiContextPolicy`; ApiAgentRuntime suite
  unchanged and passing; new suites Runtimes.ApiTurnLoop, ApiTerminalPolicy, ApiContextPolicy (23 cases). Merged head:
  10 affected suites 209/209.
- [x] **RS-7.12** Audit Mux, OpenCode, and Pi runtimes for the same failure modes (text tool calls, reasoning
  leakage into the final message, error text reaching `RuntimeProviderErrorParser`). Fix or document each gap.
  Evidence: (7963adf69) fixed: `ReasoningText.Strip` on every CLI final message
  and on Mux's own file; OpenCode reply rebuilt from JSON text events (`OpenCodeFinalMessage`); OpenCode stdout error
  events parsed (`TryParseOpenCodeJsonLine`); `--dir` dropped (OpenCode 2 rejects it). Documented in the
  MODEL_HETEROGENEITY.md audit table: CLI text tool calls cannot be executed by Armada; OpenCode 2 replaced `--variant`
  with a `#variant` suffix. Suites ReasoningText, OpenCodeRuntime, RuntimeProviderErrorParser 41/41, PiRuntime 31/31,
  MuxRunOutcome/MuxRuntime/AgentRuntimeFactory 32/32.
- [x] **RS-7.13** Harbor compatibility. The runtime changes run on Harbor in split mode. Add a Harbor capability flag
  for each new runtime behavior; a 1.5.0 Admiral with a 1.4.0 Harbor falls back to 1.4.0 behavior and shows "Harbor
  update recommended" in the Harbor and captain views. Update `docs/HARBOR_PROTOCOL.md`. Evidence: (e97124297, d382bbc4a) `HarborCapabilityFlags` with `Supports`/`Missing`/`Advertised`;
  handshake ack `missingCapabilities`; REST Harbor `missingCapabilities`/`updateRecommended`, Captain
  `harborUpdateRecommended` (`CaptainHarborUpdateResolver`); shown on dashboard, TUI, mobile and Harbor app;
  HARBOR_PROTOCOL.md flag table. RS-7.8's flag folded into the helper and advertised. Merged head: 14 Harbor, runtime
  and client suites 243/243 after the merge fixes. Wave 2 flags (text tool-call recovery, malformed-call retry,
  repetition guard, reasoning hygiene, warm-up) are advertised when those items land.

- [x] **RS-7.14** Edit and write tools keep a UTF-8 byte order mark and line endings (`EditFileTool.cs:94,150`,
  `MultiEditTool.cs:111,184`, `WriteFileTool.cs:93` read and write with no encoding; Mux fixed the same bug in 1.4.1,
  HDR Tooling 1). Battery byte-comparison test. Evidence: (b80716233) `TextFileCodec`/`TextFileSnapshot` keep BOM, encoding and CRLF/LF for edit_file,
  multi_edit and write_file; suite `Runtimes.ToolFileFormat` compares bytes (15 cases).
- [x] **RS-7.15** Reject edits whose text carries `read_file` line-number prefixes (gpt-oss:20b pasted them into code,
  HDR and MH A4), with an error the model can act on; optionally a per-profile read format without numbers.
  Evidence: (ce948cebd) `LineNumberPrefix` rejects with `line_number_prefix` (edits and write_file);
  numberless read format per profile not added (tool descriptions say numbers are display only). Suite
  `Runtimes.EditLineNumberPrefix` (8 cases).
- [x] **RS-7.16** Cold loads: warm the model with `keep_alive` before the first turn, and classify "model loading" and
  proxy time-to-first-byte timeouts (Conductor's 504 after 300 s, seen 2026-11-25 on inkling-2-mini) as Transient
  (HER:353, HER:685, HDR gpt-oss:120b cold start). Evidence: more loading phrases read as `model_loading`, a 504
  or upstream/gateway timeout is the new Transient `proxy_timeout`; `OllamaModelWarmup.WarmAsync` sends a prompt-less
  `/api/generate` with `keep_alive` (Ollama only, failures returned as results); simulator kinds `ModelLoading500` and
  `ProxyFirstByteTimeout504`; tests `cold_load_and_proxy_timeout_are_transient`, `ollama_warmup_sends_keep_alive`
  (812b4c944). The runtime calls it before an Ollama endpoint's first turn (timeout is the first-response deadline,
  300 to 3600 s; failure only warns), Harbor flag `api-model-warmup-v1` (5e634c424).
- [x] **RS-7.17** Ollama's "token repeat limit reached" 500 (HDR, gpt-oss:20b) becomes a simulator kind (RS-1.1) and is
  handled like RS-7.3. Evidence: maps to `degenerate_output` (TaskFailure) with simulator kind `TokenRepeatLimit500`;
  `OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration` covers it and the malformed tool-call 500 for RS-7.3. Tests
  `Runtimes.OpenModelSimulator.token_repeat_limit_is_degenerate_task_failure`, `api_runtime_ends_on_tokenrepeatlimit500`
  (812b4c944).

## Phase 8: Capability-aware routing and prompts

Armada already measures models (probes, battery, profiles, scorecards). Almost none of it reaches routing or prompt
rendering. This phase closes that loop.

- [ ] **RS-8.1** Probes fill capabilities. After a passing tool-calling probe, `ModelProbeService` writes
  `ToolsAvailable` and `McpAvailable`; served context from `/api/ps` or RS-7.8 is written too. Manual values still
  win. The envelope's `requiredTools` then works without hand edits. Evidence:
- [ ] **RS-8.2** Apply `ModelProfile.Rendering` (`Template`, `RepeatContractAtEnd`, `MaxFilesPerMission`), which is
  stored today but never read (`ModelProfileAccessService.cs:134-140`). Evidence:
- [ ] **RS-8.3** Prompt variant resolution order: voyage override, captain option, model profile rendering, then
  automatic from served context (Compact below a threshold setting). `PromptVariantResolver.Resolve` consults
  context tokens. Evidence:
- [ ] **RS-8.4** Context as a hard routing filter: a captain whose served context cannot hold the estimated prompt
  (instructions budget plus handoff plus contract) is not eligible, with a typed blocker. Evidence:
- [ ] **RS-8.5** Tier from data. Effective tier = explicit captain tier, else the model profile's tier (set by hand or,
  behind a setting, proposed from scorecard data once it has enough samples), else the name regex. Evidence:
- [ ] **RS-8.6** Rank within a tier by per-persona outcomes from `StageOutcome` and `ModelScorecardService`
  (Judge pass rate, no-op rate, tool-call validity, repair rate), with a minimum sample size and decay. Replaces the
  current `tier, ActiveMissionCount, LastUpdateUtc` order (`CaptainRouting.cs:305`) only when data exists. Selection
  telemetry records why. Evidence:
- [ ] **RS-8.7** Judge strength rule (per pipeline or persona, default on for new pipelines and off for existing ones
  to keep 1.4.0 behavior): the Judge's effective tier is at least the highest tier among the stages it reviews, and
  a different `ModelFamily` is preferred (reuse `EvaluatorSelector`). `ExcludeReviewAuthorsFromJudge` compares model
  identity, not captain id. When no captain satisfies the rule, the mission waits with a typed blocker or, if the
  policy says so, proceeds and is flagged (G8). Evidence:
- [ ] **RS-8.8** Per-profile timeouts: stall threshold, tool-wait hard limit, maximum runtime, and first-token time,
  with defaults scaled by measured tokens per second from probes. Global settings stay as defaults. Evidence:
- [ ] **RS-8.9** Endpoint concurrency key becomes (Harbor or Admiral, normalized URL, model) instead of the base URL
  (`ConcurrencyCapacityPolicy.EndpointKey`), so `localhost` on two Harbors are two limits and `127.0.0.1` and
  `localhost` are one. Evidence:
- [ ] **RS-8.10** Default instructions file per runtime: map Codex to `AGENTS.md` if that is what it reads natively
  (verify), and stop defaulting unknown runtimes to `CLAUDE.md` (`MissionPromptBuilder.cs:74-86`). Evidence:

## Phase 9: Landing and the merge queue

- [ ] **RS-9.1** Non-fast-forward push rejection is requeued from a fresh fetch a bounded number of times (setting,
  default 3) before failing, instead of the hard-coded `Infra` at `MergeQueueService.cs:726`. Same for the lost
  handoff case at `AdmiralService.cs:2243`. Detect it from the git exit status through a typed result, not text.
  Evidence:
- [ ] **RS-9.2** Merge three landing sweeps (`LandingReconciler`, `ManualLandingReconciler`,
  `LandingIntegrityService`) into one reconciler with one scan per tick and explicit cases, including the RS-2.3
  promotion. Evidence:
- [ ] **RS-9.3** Fetch once per (vessel, target branch) group per pass instead of per entry. Evidence:
- [ ] **RS-9.4** Optional batched landing (setting, default off in 1.5.0, confirmed by Joel 2026-11-25): merge N
  entries, verify once, bisect on failure. Benchmarked against serial landing. Evidence:

## Phase 10: Performance

Most performance work lands in Phase 4. These are the remaining hot spots and the regression gate.

- [x] **RS-10.1** Orphan-dock reclaim (`AdmiralService.cs:2098-2129`): hoist the five status scans out of the
  per-dock loop into one set of active dock ids. Evidence: 5c361f92b; five status scans hoisted out of the per-dock loop, run once per sweep and only when a dock needs them.
- [x] **RS-10.2** Time-bounded scans: the recovery coordinator and similar sweeps query with `updatedSince` on all
  four providers instead of loading every Failed mission and filtering in memory. Add the indexes they need.
  Evidence: 5c361f92b; `EnumerateByStatusAsync(status, updatedSince)` on all four providers, migration 143 `idx_missions_status_last_update`; recovery coordinator reads the last 24 hours. Other status scans need old rows, belong to Phase 9, or are doctor counts. DB suites 86/86 on PostgreSQL, MySQL, SQL Server.
- [x] **RS-10.3** `CaptainEscalationSelector` and `ReviewAuthorCaptainIdsAsync` read profiles and parents in one
  query, not one per captain or parent. Evidence: 5c361f92b; `ModelProfiles.ReadManyAsync` (chunked at 500) and one voyage summary read for review authors.
- [x] **RS-10.4** Dispatch advisor loads its vessel context once per voyage creation and adds each new mission to it,
  which also catches overlaps inside the same voyage (`BlackBoxDispatchAdvisor.cs:59`,
  `MissionOverlapAnalyzer.LoadAsync`). The analyzer prefers declared `OwnedPathsJson` over files guessed from prose.
  Evidence: 5c361f92b; `MissionDispatchBatch` loads vessel context once per voyage (one load for four missions in test); owned paths win over brief-guessed files.
- [x] **RS-10.5** `BoundedTextBuffer.Trim` becomes a ring buffer (no full `ToString()` per line over the cap).
  Evidence: 5c361f92b; ring buffer, output identical to the old trim on random input; 6,000 lines 192 ms to 8 ms.
- [ ] **RS-10.6** Benchmark gate: rerun RS-0.7, save under `benchmarks/baselines/1.5.0/`, and record G10 and G11.
  Any regression against 1.4.0 on another benchmark is explained or fixed. Evidence:

## Phase 11: Code health

Done last among the backend phases because earlier phases already remove most of the duplication. These items finish
the job and keep it from coming back.

- [ ] **RS-11.1** Decompose `MissionService` (about 8,700 lines in 25 partial files sharing 15+ mutable dictionaries)
  into classes with one responsibility each, following what Phases 3 to 6 created: `MissionTransitions`,
  `AdmissionPipeline`, `MissionLauncher`, `StageCompletionHandler`, `HandoffService`, `ReworkService`,
  `OutputRepairService`, `ArchitectFanOutService`. No partial shares mutable state with another. Same for the
  orchestration parts of `AdmiralService` (3,272 lines). Evidence:
- [ ] **RS-11.2** Style fixes: `var` and tuples in `MissionService.cs` (around line 548), `MissionService.Readiness.cs:11`,
  and `AdmissionStuckMonitor.cs:57-66`; the mis-indented blocks at `MissionService.Handoff.cs:162-178`; inline
  non-compiled regexes in `MissionService.ArchitectPlan.cs:317, 547, 577`. Evidence:
- [ ] **RS-11.3** Rename `SlotManager` to describe what it manages (binary rebuild slots). Evidence:
- [ ] **RS-11.4** Source-scan tests that keep the simplifications in place: no direct mission status writes (RS-3.6),
  no reads of control markers from `Description`, no `ex.Message.Contains` in Core, no second dispatcher. Evidence:
- [ ] **RS-11.5** Warning-free Release build on both frameworks after all changes. Evidence:

## Phase 12: Product surfaces

Every backend change above that a person can see or configure appears on every surface that shows that kind of data.
Parity manifests (`src/Armada.Tui/parity.json`, `src/Armada.Mobile/parity.json`) are updated with each item.

### Server and APIs

- [ ] **RS-12.1** REST: new and changed endpoints and fields for recovery policy (RS-5.5), escalation ladder (RS-5.6),
  judge strength rule (RS-8.7), per-profile timeouts (RS-8.8), discovered context (RS-7.8), mission attempt
  history with failure kind, repairs, and escalations (RS-1.4), `Persona` and `Mode` on mission requests (RS-4.2),
  reliability gate report (RS-1.5), and submitted stage results (RS-6.2). Request validation and authorization per
  `AUTHENTICATION.md`, tenant scoping tested. Evidence:
- [ ] **RS-12.1a** Battery runs have no list or cancel route (found 2026-11-25: stopping the gate client left its run
  executing on the Admiral). Add `GET /api/v1/battery/runs` (paged, filters) and `POST /api/v1/battery/runs/{id}/cancel`
  (cancels open missions and releases docks, as a Full Evaluate stop does), with MCP, SDK, Postman, CLI, and dashboard
  surfaces; the gate runner cancels its run on Ctrl+C. Evidence:
- [ ] **RS-12.2** MCP: matching tools and fields, plus the submit tools (RS-6.2). Mark which tools a captain may call
  and which are operator tools. Evidence:
- [ ] **RS-12.3** WebSocket: events for escalation, repair, and attempt changes; existing `mission.changed` payloads
  carry the new fields; visibility rules from 1.4.0 apply. Evidence:
- [ ] **RS-12.4** Settings: every new setting has validation in its setter, defaults documented, and appears in the
  runtime settings API and dashboard Settings page. Evidence:
- [ ] **RS-12.5** Telemetry: Prometheus metrics for RS-1.4, Grafana dashboard panels added under
  `docker/armada/observability/grafana` (pipeline reliability: completion rate, failure kinds, repairs, escalations,
  dispatch latency), per `TELEMETRY_REQUIREMENTS.md`. Evidence:

### SDK and Postman

- [ ] **RS-12.6** `Armada.Client` (.NET SDK): typed models and methods for every RS-12.1 change, XML docs, contract
  tests against a live server (`Client.Contract.*` suites), loopback base URLs use `127.0.0.1`. Evidence:
- [ ] **RS-12.7** Postman: `Armada.postman_collection.json` updated with folder- and request-level descriptions for
  every new or changed request, variables for base URL and credentials, no em-dashes; the open-model simulation
  (RS-1.1) documented in `postman/README.md`; regenerate `docs/api-surface-1.0.json` and pass the API contract
  test. Evidence:

### CLI and TUI

- [ ] **RS-12.8** `armada` CLI (Armada.Helm): commands and flags for persona escalation ladders, model profile
  recovery policy and timeouts, mission attempt history (`armada mission attempts <id>`), mission create `--persona`
  and `--mode`, and the gate report (`armada battery report`). Help text and docs. Evidence:
- [ ] **RS-12.9** TUI: mission detail shows attempts, failure kinds, repairs, and escalations; persona and pipeline
  screens edit the ladder and judge rule; model profile screen shows recovery policy, timeouts, and discovered
  context; blockers show the typed RS-4.6 reasons. `scripts/tui/generate-parity-manifest.py --check` passes.
  Evidence:

### Dashboard

All dashboard work follows `DASHBOARD_STYLE_AND_USABILITY.md` and `FRONTEND_ARCHITECTURE.md`, is localized for every
shipped locale per `I18N.md`, and is checked in light and dark themes at desktop and phone widths.

- [ ] **RS-12.10** Mission detail: an attempts timeline (each attempt's captain, model, tier, failure kind, repair
  kind, escalation, duration), the submitted structured result, and findings with severity. Evidence:
- [ ] **RS-12.11** Persona and pipeline editors: escalation ladder editor (same interaction as Captain Priorities),
  judge strength rule, and reference pipeline templates (RS-1.6) available as "create from template". Evidence:
- [ ] **RS-12.12** Model profiles and endpoints: recovery policy, timeouts, rendering, discovered versus configured
  context, probe-derived capabilities with their source, streaming toggle. Evidence:
- [ ] **RS-12.13** A reliability view (under Captains or Insights, per the navigation model in
  `docs/DASHBOARD_NAVIGATION.md`): per model and persona completion, failure kinds, repairs, escalations,
  `Unclassified` rate, and the latest gate report. Backend filtering and sorting. Evidence:
- [ ] **RS-12.14** Voyage and mission creation forms: persona and mode fields on every path; blockers on pending
  missions show the typed reason and the action that clears it. Evidence:
- [ ] **RS-12.15** Dashboard tests (Vitest) for every new view and editor; `npm run build` and `npm run test:run`
  green; Playwright visual check (light, dark, phone, one non-English locale, no horizontal overflow, no console
  errors) with screenshots saved. Evidence:

### Mobile

- [ ] **RS-12.16** Mobile app: mission detail attempts timeline and structured findings, blockers with typed reasons,
  read-only recovery policy and escalation ladder on persona and model views, push notifications for escalation and
  recovery-exhausted events (respecting existing notification categories). Localized and accessible. Evidence:
- [ ] **RS-12.17** Mobile tests: `npm run typecheck`, `npm run lint`, `npm run test:ci`, `npm run test:android`,
  route screens, parity manifest, and Maestro flows (`scripts/mobile/run-e2e.sh`) for the new screens. Evidence:

### VS Code extension, Harbor, Proxy

- [ ] **RS-12.18** VS Code extension (`src/Armada.VSCode`): mission views show attempts, failure kinds, escalations,
  and blockers; dashboard pages in editor tabs pick up the new views. Unit and integration tests. Update its
  `CHANGELOG.md` and `README.md`. Evidence:
- [ ] **RS-12.19** Harbor app: shows Harbor capability flags (RS-7.13) and an update prompt when the Admiral needs a
  newer Harbor; discovered context for local endpoints. Evidence:
- [ ] **RS-12.20** Proxy: confirm every new REST, WebSocket, and MCP route passes through the tunnel and portal
  unchanged (route allow-lists, payload size limits). Update `docs/PROXY_API.md` if anything changes. Evidence:

## Phase 13: Documentation

Documents written for people follow `WRITING_DOCUMENTS.md`: prose that explains, not only lists; no em-dashes; a
humanizer pass and a manual check against the Signs of AI writing page before an item is complete.

- [ ] **RS-13.1** New `docs/OPEN_MODELS.md`: how to run Qwen3.8 (and the second family) in a pipeline with Opus and
  GPT-6.1. Endpoint setup for Ollama, vLLM, and LM Studio; served context; streaming; the reference pipelines; what
  the escalation ladder and judge rule do; what the gate measured for 1.5.0 with the real numbers; known limits.
  Evidence:
- [ ] **RS-13.2** `docs/MODEL_GUIDANCE.md`: replace the "tool-calling reliability is the model's responsibility"
  stance with what Armada now absorbs (RS-7.2 to RS-7.6) and what still belongs to the model. Evidence:
- [ ] **RS-13.3** `docs/MODEL_HETEROGENEITY.md`: update "Where Armada stands today", the failure taxonomy mapping
  (RS-5.1), escalation with memory (RS-5.7), rendering (RS-8.2, RS-8.3), and the scorecard feeding routing (RS-8.6).
  Evidence:
- [ ] **RS-13.4** `docs/CAPTAIN_ROUTING.md`, `docs/PERSONAS.md`, `docs/PERSONAS_GUIDE.md`, `docs/PIPELINES.md`:
  escalation ladders, judge strength rule, capability filters, ranking, the admission gate order, typed blockers.
  Evidence:
- [ ] **RS-13.5** `docs/REST_API.md`, `docs/MCP_API.md` (including the submit tools and their schemas),
  `docs/WEBSOCKET_API.md`, `docs/PROXY_API.md` if changed, `docs/HARBOR_PROTOCOL.md`. Evidence:
- [ ] **RS-13.6** `docs/OPERATIONS.md` (every new setting with default, minimum, maximum, and effect),
  `docs/UPGRADING.md` (migration 133 and its backfill, behavior changes, Harbor version advice, the legacy plan
  format deprecation), `DEPRECATION.md`. Evidence:
- [ ] **RS-13.7** `docs/MERGING.md` (requeue on non-fast-forward, single reconciler, batching), `docs/BLACKBOX.md`
  if rule hints change, `docs/CONCURRENCY_TESTING.md` and `docs/PERFORMANCE.md` (single dispatcher, snapshot,
  new benchmarks). Evidence:
- [ ] **RS-13.8** `docs/TESTING.md`: the open-model simulator, crash-point harness, seeded-defect tasks, gate report,
  and how to run each. `docs/RELEASING.md`: add the reliability gate as a release step; fix its statement that the
  dashboard `dist/` is committed (it is git-ignored, per `CLAUDE.md`). Evidence:
- [ ] **RS-13.9** `docs/TUI.md`, `docs/MOBILE.md` (new screens; store submission section rewritten from what
  Phase 16 actually did, without private ids), `docs/DASHBOARD_NAVIGATION.md`, `src/Armada.VSCode/README.md`,
  `docs/INSTRUCTIONS_FOR_*.md` and `docs/*_AS_ORCHESTRATOR.md` where the submit tools or contracts change what an
  orchestrator sees. Evidence:
- [ ] **RS-13.10** `README.md`, `DOCKERHUB_README.md`, `GETTING_STARTED.md`, `FAST_TRACK_SETUP.md`: mention mixed
  open and frontier pipelines and link `docs/OPEN_MODELS.md`. Evidence:
- [ ] **RS-13.11** `CHANGELOG.md`: every user-visible change, setting, migration, deprecation, and behavior change
  under the 1.5.0 entries, with the before and after gate numbers. Evidence:
- [ ] **RS-13.12** Update `CLAUDE.md` and `AGENTS.md` architecture notes where this plan changed the architecture they
  describe (new services replacing `MissionService` partials, single dispatcher, submit tools). Joel approved
  editing `AGENTS.md` on 2026-11-25; keep the two files in sync in the same commit. Evidence:

## Phase 14: Verification

- [ ] **RS-14.1** Full local run: warning-free Release build; `Test.Automated` net8.0 and net10.0; xUnit and NUnit
  adapters; dashboard; mobile; TUI parity; VS Code extension. Record counts. Evidence:
- [ ] **RS-14.2** `full.yml`, `security.yml`, `db-parity.yml` (`RESULT: PARITY OK`), and `install-verify.yml` green on
  the release candidate commit. Evidence:
- [ ] **RS-14.3** Upgrade test from the v1.4.0 baseline on all four providers (`scripts/common/run-upgrade-test.sh
  --from-ref <v1.4.0 commit> --providers all`), with seeded missions in every text-marker state so the RS-3.1
  backfill is exercised. Evidence:
- [ ] **RS-14.4** Crash-point matrix (RS-1.3): every named point, every provider, recorded. Evidence:
- [ ] **RS-14.5** Simulated user testing per `SIMULATED_USER_TESTING.md` on an isolated `armada-usertest` stack,
  personas including an operator setting up Qwen3.8 for the first time from `docs/OPEN_MODELS.md` alone. S1 and S2
  findings fixed; lower ones fixed or added to `docs/BACKLOG.md` with a reason. Evidence:
- [ ] **RS-14.6** Acceptance gate (G1 to G12) on the release candidate with the reference configuration from RS-0.4,
  all four reference pipelines, every battery pack, with the run counts in [Run budget](#run-budget). Save the RS-1.5 report under `benchmarks/results/1.5.0/` with model names
  but no hosts, tokens, or keys. Evidence:
- [ ] **RS-14.7** Two weeks of real use: run the maintainer's own repositories through at least P-A and P-D. Any
  intervention gets a cause and an item. Evidence:
- [ ] **RS-14.8** Final diff review for requirements compliance, tenancy leaks, unsafe fallbacks, em-dashes
  (`grep -rnP '\x{2014}'` over the changed files), private data, documentation drift, and unintended edits. Evidence:

## Phase 15: Release (server, installers, images, extension)

Follow `docs/RELEASING.md` and the runbook section 16.4. Stop at each approval point.

- [-] **RS-15.1** [DECISION] Release shape. Answer (2026-11-25): 1.5.0 gets a `v1.5.0` tag and a GitHub Release with
  installers; signing, NuGet, and Open VSX stay off. Still required: Joel's go-ahead on the specific candidate commit
  once RS-14 is green. Answer:
- [ ] **RS-15.2** Move `## Unreleased` entries under `## v1.5.0 (<date>)` in `CHANGELOG.md`. Evidence:
- [ ] **RS-15.3** Merge `feature/v1.5.0` to `main` when Joel asks (pull request, CI green). Evidence:
- [ ] **RS-15.4** Docker images: `./build-all.sh v1.5.0`; Docker Hub shows `v1.5.0` and `latest` for server,
  dashboard, and proxy, amd64 and arm64. Evidence:
- [ ] **RS-15.5** Tag `v1.5.0` and push the tag (after the RS-15.1 go-ahead), watch `release.yml`, verify the
  installers for Windows, macOS, and Linux and `SHA256SUMS` (runbook 16.4 steps 5 and 6), and write the release page
  per `INSTALLERS.md` "Writing the release page", including the unsigned-build notices. Evidence:
- [ ] **RS-15.6** VS Code extension: `./build-vscode.sh`, publish by hand (runbook 16.5), confirm with
  `npx @vscode/vsce show LatticeAI.armada-ai`. Evidence:
- [ ] **RS-15.7** Proxy: deploy only if Phase 12 changed the proxy (runbook 16.1 and section 17). Evidence:
- [ ] **RS-15.8** Website `armadago.ai`: mention mixed open and frontier pipelines and link the docs (runbook 16.2).
  Evidence:
- [ ] **RS-15.9** Post-release checks in the runbook section 17; add a line to the runbook's document history
  (section 21). Signing, NuGet, and Open VSX stay out of scope unless Joel says otherwise. Evidence:

## Phase 16: Mobile rebuild and store submission

The app has never been in a store; every one-time step in the runbook section 12.12 and `INSTALLERS.md` ("Mobile
apps: Expo and EAS", "Apple App Store", "Google Play Store") is still open. Steps tagged [HUMAN] need Joel at a
console or a two-factor prompt. Record build and submission URLs in `src/Armada.Mobile/EXPO.md` and private ids in
`src/Armada.Mobile/store-private.local.md` (git-ignored), never in this file.

### Prepare (agent)

- [ ] **RS-16.1** Bundle ID and Android package become `ai.latticetech.armada` in `app.config.ts` (permanent once a
  store record exists). Add the `*_LOCAL_TEAM_ID` switch used by Mux Cloud so personal-team device builds append
  `.dev` and drop the push and associated-domains entitlements, instead of editing the real ID. Evidence:
- [ ] **RS-16.2** `eas.json` to the family standard: `development` (plus `development-device`), `preview` as a store
  build (`"distribution": "store"`), `production`; `appVersionSource: remote` with `autoIncrement: true` on store
  profiles; `APP_VARIANT` per profile and read by the app config; Android submit `track: internal` and
  `releaseStatus: draft` for both profiles; iOS submit `appleTeamId` and `ascAppId` filled after RS-16.6 (no
  `appleId`). Set `owner` and the EAS project id in the app config with environment overrides. Evidence:
- [ ] **RS-16.3** `.gitignore` covers `secrets/`, `*.p8`, `*.key`, `*.local.md`, `google-services.json` where
  required; nothing with a password is committed. Evidence:
- [ ] **RS-16.4** Version 1.5.0 in `app.config.ts`; `extra.releaseVersion` set; build numbers come from EAS remote
  versioning. Evidence:
- [ ] **RS-16.5** Store readiness checks from `INSTALLERS.md` "What review needs from every app":
  - Real content on first launch: an always-on demo Admiral for reviewers with representative fleets, voyages, and
    missions (decide where it runs in RS-16.9), and a reviewer account whose credentials go only in
    `store-private.local.md` and the consoles.
  - Account deletion: confirm whether the app can create accounts. If it can, add in-app deletion (Apple 5.1.1(v))
    and a public web deletion URL; if it only signs in to an Admiral the user runs, document that for review.
  - No outside purchase paths; copy My Morning News's policy test.
  - Privacy answers match the binary; no tracking; `usesNonExemptEncryption: false` stays.
  - Android: no `AD_ID` permission in the merged manifest; current target API level.
  Evidence:
- [ ] **RS-16.6** Store assets in `src/Armada.Mobile/store/`: iPhone 6.9" (1320x2868) and iPad 13" (2064x2752)
  screenshots of real views including the new reliability screens, Play 512x512 icon, 1024x500 feature graphic,
  phone screenshots; store copy with no em-dashes; support contact per `INSTALLERS.md`. Evidence:

### One-time console setup ([HUMAN] unless noted)

- [ ] **RS-16.7** [HUMAN] `npx eas-cli login` and `npx eas-cli init` in `src/Armada.Mobile` under the family Expo
  account (runbook 12.2); record the project id in the app config (agent). Evidence:
- [ ] **RS-16.8** [HUMAN] Apple: register the App ID `ai.latticetech.armada` under the family team (runbook 12.3),
  then create the App Store Connect record (store name availability checked first, fallback name ready, SKU
  `armada-ios`). Agent fills `ascAppId` and `appleTeamId` in `eas.json` (RS-16.2). Evidence:
- [x] **RS-16.9** [DECISION] Where the reviewer demo Admiral runs. Answer (2026-11-25): next to the proxy on the
  existing EC2 proxy host, reached through the proxy.
- [ ] **RS-16.9a** Demo Admiral build-out (agent, with [HUMAN] for host access if needed): a compose file under
  `docker/demo/` (healthcheck, `depends_on`, `.yaml`, `update` helper per `REPOSITORY_REQUIREMENTS.md`) running
  `armada-server` 1.5.0 with remote control pointed at the proxy; a seeding script built on
  `src/Armada.PerfSeed` or the REST API that creates representative fleets, vessels, captains, voyages, and missions
  in every status; a scheduled reset that restores the seed nightly; captains that cannot execute real work or reach
  real repositories; resource limits so it cannot starve the proxy. Deploy per the runbook section 11 procedure;
  reviewer credentials only in `store-private.local.md` and the store consoles. Verify the mobile app connects
  through the proxy and shows real content on first launch. Update the runbook sections 11 and 21. Evidence:
- [ ] **RS-16.10** [HUMAN] Firebase project with an Android app for `ai.latticetech.armada`; FCM V1 key uploaded with
  `npx eas-cli credentials`; agent adds `googleServicesFile` handling without committing the file. APNs key attached
  for iOS push. Evidence:
- [ ] **RS-16.11** [HUMAN] Google Play Console: create the app under the Lattice AI organization account, package
  `ai.latticetech.armada`. Evidence:

### Build and submit

- [ ] **RS-16.12** Pre-build gate: RS-12.17 green; `expo export` succeeds; Maestro flows pass on an iOS simulator and
  an Android emulator. Evidence:
- [ ] **RS-16.13** [HUMAN] First iOS build interactively: `npx eas-cli build -p ios --profile production` (Apple
  sign-in and two-factor; accept the distribution certificate, provisioning profile, and push setup). Evidence:
- [ ] **RS-16.14** [HUMAN] First iOS submission interactively:
  `npx eas-cli submit -p ios --profile production --latest`, letting EAS generate the App Store Connect API key
  (ADMIN role). Later submissions are non-interactive. Evidence:
- [ ] **RS-16.15** [HUMAN] TestFlight: after processing, add internal testers and test on real devices: sign-in
  (direct and through the proxy), Face ID, push, the new reliability screens. Evidence:
- [ ] **RS-16.16** Android build: `npx eas-cli build --profile preview --platform android` (agent can run it once
  EAS is set up). Evidence:
- [ ] **RS-16.17** [HUMAN] Upload that first `.aab` by hand to the Play internal testing track (Google refuses API
  uploads before one manual upload); create the Google Cloud service account with release permissions and hand
  its key to EAS. Evidence:
- [ ] **RS-16.18** [HUMAN] Register Play App Signing SHA-1 and SHA-256 wherever the app's signature is checked
  (Firebase at least). Evidence:
- [ ] **RS-16.19** Later builds and submissions non-interactively (agent):
  `npx eas-cli build -p all --profile production --non-interactive`, then
  `npx eas-cli submit -p ios --profile production --latest --non-interactive` and the Android equivalent to the
  internal track. Record URLs in `EXPO.md`. Evidence:
- [ ] **RS-16.20** [HUMAN] Internal testing on Android devices, same checklist as RS-16.15. Evidence:

### Listings and review

- [ ] **RS-16.21** [HUMAN] App Store listing: copy, keywords, category, privacy answers, screenshots, reviewer
  account and notes (pointing at the demo Admiral), then submit `production` for review. The agent can prepare the
  listing through the App Store Connect API where `INSTALLERS.md` describes it. Evidence:
- [ ] **RS-16.22** [HUMAN] Play listing: store listing, data safety form answered from the merged manifest of the
  production build, content rating, target audience, reviewer access; promote from internal to production with
  `releaseStatus: draft` and release by hand. Evidence:
- [ ] **RS-16.23** Respond to review feedback; record what each store said in `INSTALLERS.md` (the family document
  asks for this after the first submission) and in the runbook. Evidence:
- [ ] **RS-16.24** Update the runbook (outside the repository) sections 1, 12, 16.6, and 21 with the real state:
  app records, tracks, build numbers, dates. Update `docs/MOBILE.md` "Building and store submission" with the
  procedure as it actually ran, without private ids. Evidence:
- [x] **RS-16.25** [DECISION] Over-the-air updates. Answer (2026-11-25): not in 1.5.0; a later release. Add an entry
  to `docs/BACKLOG.md` (EAS Update with fingerprint runtime policy and code signing) as part of RS-13.11.

---

## Traceability: review findings to items

Every finding from the 2026-11-25 review maps to at least one item. If an item is dropped, the finding it covered
must move to another item or be recorded in `docs/BACKLOG.md` with a reason.

| Review finding | Items |
|---|---|
| Two dispatch loops; synchronous head-of-queue dispatch; 10 s latency | RS-4.5, G10 |
| Two voyage dispatch bodies; four overloads; REST/MCP/WebSocket drift; REST lacks Persona and Mode | RS-4.1, RS-4.2 |
| Voyage, fan-out, and handoff creation not atomic | RS-4.3, RS-2.8 |
| Per-tick database load; capacity pre-check disagrees; three capacity calculations; global lock | RS-4.6 to RS-4.9, G11 |
| State machine advisory; 65 direct writes | RS-3.5, RS-3.6, RS-11.4 |
| Control state in description text (escalation, fan-out, rework, repair markers, title lookup) | RS-3.1 to RS-3.4 |
| In-memory state lost on restart | RS-3.7 |
| Crash recovery: Assigned burns retries; launch without PID | RS-2.7, RS-4.10 |
| Seven failure taxonomies; five mapping sites; four recovery loops; three landing sweeps | RS-5.1, RS-5.10, RS-9.2 |
| Unrecognized errors become Crash and get no recovery | RS-5.2, RS-5.3, RS-2.10, G6 |
| Local 429 quarantines the captain | RS-5.4 |
| Escalation one hop, voyage only, not stronger, tracked in prose | RS-5.6, RS-3.2, G12 |
| Rescues same tier, same captain, no output tail | RS-5.8, RS-5.7 |
| Global retry budgets; hard-coded rework rounds | RS-5.5 |
| RecoveryExhausted fires on the wrong event; incidents closed without verification | RS-5.9 |
| Exception message matching | RS-5.11, RS-11.4 |
| Stranded mission after landing; lease not renewed; non-fast-forward as Infra; serial queue | RS-2.3, RS-2.5, RS-9.1, RS-9.3, RS-9.4 |
| Rescue incidents not crash-consistent | RS-2.6 |
| Full stage rerun for format errors | RS-6.4, G4, G5 |
| No structured output or tool submission | RS-6.2, RS-6.3 |
| Contract text duplicated and inconsistent; Architect prompt gives two formats | RS-6.1 |
| Plan parser brittle (fences, number strings, case, error detail) | RS-6.5 |
| Verdict recovery needs "ARMADA"; SPEC_CONFLICT not recovered; truncated fences; marker strictness mismatch | RS-6.6, RS-2.12 |
| Findings scraped from headings; severity bug | RS-6.7, RS-2.1 |
| Shallow PASS gate | RS-6.8, G7 |
| Six copies of handoff state; three artifact creation paths; length-based dedup | RS-6.9, RS-3.4 |
| Judge sees only the previous stage | RS-6.10 |
| Prompt assembly sniffs content; no whole-file budget; dead caps | RS-6.11 |
| Legacy `[ARMADA:MISSION]` regex path | RS-6.12 |
| Text tool calls never recovered; Ollama 500 retried identically; misleading unknown-tool error; no repetition guard | RS-7.2 to RS-7.5 |
| Reasoning leakage | RS-7.6 |
| Streaming off; global timeouts | RS-7.7, RS-8.8 |
| Context not discovered; fixed tool caps; compaction keeps assistant bodies; chars/4 | RS-7.8 to RS-7.10 |
| `RunLoopAsync` too large; mutable static limits | RS-7.11, RS-7.9 |
| Profiles do not drive prompts or routing; probes do not fill capabilities; tier by regex | RS-8.1 to RS-8.6, RS-2.2 |
| No strong-judge rule; author exclusion by captain id | RS-8.7, G8 |
| Endpoint concurrency key too coarse | RS-8.9 |
| Claude-specific instructions file default | RS-8.10 |
| Planning loses objective | RS-2.4 |
| Ownership and tenancy on REST voyages | RS-2.9 |
| Hierarchy rules in four places; ownership overlap only for Architect | RS-4.4 |
| Dispatch advisor reloads per mission; ignores declared ownership | RS-10.4 |
| Orphan-dock O(docks x missions); unbounded scans; per-captain profile reads | RS-10.1 to RS-10.3 |
| `BoundedTextBuffer` O(n^2) | RS-10.5 |
| `MissionService` god class; style violations; misleading `SlotManager` name; dead code | RS-11.1 to RS-11.3, RS-2.2 |

## Decisions log

Record each [DECISION] answer here as well as inline, with the date.

| Item | Question | Answer | Date |
|---|---|---|---|
| RS-0.2 | Release version for this work | 1.5.0, branch `feature/v1.5.0` | 2026-11-25 |
| RS-0.3 | Gate thresholds | G1 at least 99%; G2 to G12 as drafted | 2026-11-25 |
| RS-0.4 | Open model reference configuration | Qwen3.8 27B on ApiEndpoint, Ollama `/api/chat` through Conductor; also DeepSeek 4.1, GLM, inkling-2-mini; Ollama Cloud fallback; details in the local file only | 2026-11-25 |
| RS-1.6 | Frontier runtimes | Opus on Claude Code, GPT-6.1 on Codex | 2026-11-25 |
| RS-9.4 | Batched landing default | Off | 2026-11-25 |
| RS-13.12 | May the work edit `AGENTS.md` | Yes, in sync with `CLAUDE.md` | 2026-11-25 |
| RS-15.1 | Release shape | Tag `v1.5.0` and a GitHub Release; candidate commit go-ahead still pending | 2026-11-25 |
| RS-16.9 | Reviewer demo Admiral | Next to the proxy on the EC2 proxy host | 2026-11-25 |
| RS-16.25 | Over-the-air updates | Later release | 2026-11-25 |
| Run budget | Model spend | Conductor models unlimited; Ollama Cloud (DeepSeek, GLM, Kimi) frugal; Opus and GPT-6.1 few runs | 2026-11-25 |

## Validation log

Append entries newest last: date, item ids, exact command, result counts, log path. Record failures and their fixes
too; a validation entry is not complete until the command passes.

| Date | Items | Command | Result | Log |
|---|---|---|---|---|
| | | | | |
