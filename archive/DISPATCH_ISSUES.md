# Dispatch and Mission Lifecycle Findings

## Scope

This report covers the critical paths requested:

1. Goal dispatch and pipeline mission pre-creation
2. Model output capture and handoff between stages
3. Model state transition and process exit handling
4. Cleanup, dock reclamation, and captain release

I traced the implementation in:

- `src/Armada.Core/Services/AdmiralService.cs`
- `src/Armada.Core/Services/MissionService.cs`
- `src/Armada.Core/Services/CaptainService.cs`
- `src/Armada.Server/AgentLifecycleHandler.cs`
- `src/Armada.Server/MissionLandingHandler.cs`
- `src/Armada.Runtimes/BaseAgentRuntime.cs`
- `src/Armada.Core/Services/PromptTemplateService.cs`

I also checked the relevant tests in:

- `test/Armada.Test.Unit/Suites/Services/PipelineDispatchTests.cs`
- `test/Armada.Test.Unit/Suites/Services/MissionStatusTransitionTests.cs`
- `test/Armada.Test.Unit/Suites/Services/MissionPromptTests.cs`
- `test/Armada.Test.Runtimes/Suites/BaseAgentRuntimeTests.cs`

## Executive Summary

The system does pre-create pipeline missions correctly, but the actual context delivered to agents is split across two different mechanisms and is inconsistent. The largest correctness problems are:

- process exit can be dropped if the agent exits before `AgentLifecycleHandler` records the PID-to-mission mapping
- completion, landing, and dock reclaim are owned by multiple components, which creates race-prone duplicate finalization paths
- stage handoff uses one output source for persistence (`AgentOutput`) and a different one for downstream context (mission log parsing)
- persona prompt resolution is inconsistent, and `TestEngineer` falls through to the generic fallback prompt in `CLAUDE.md`

Those issues are enough to explain stalled voyages, repeated mission loops, missed clean exits, and weak cross-stage context propagation.

## What Is Working

### Pipeline pre-creation is implemented

Multi-stage dispatch is present and does create chained missions ahead of time:

- `src/Armada.Core/Services/AdmiralService.cs:186`
- `src/Armada.Core/Services/AdmiralService.cs:200`
- `src/Armada.Core/Services/AdmiralService.cs:209`
- `src/Armada.Core/Services/AdmiralService.cs:218`
- `src/Armada.Core/Services/AdmiralService.cs:219`
- `src/Armada.Core/Services/AdmiralService.cs:229`

The unit test for that path exists:

- `test/Armada.Test.Unit/Suites/Services/PipelineDispatchTests.cs:107`
- `test/Armada.Test.Unit/Suites/Services/PipelineDispatchTests.cs:154`
- `test/Armada.Test.Unit/Suites/Services/PipelineDispatchTests.cs:172`
- `test/Armada.Test.Unit/Suites/Services/PipelineDispatchTests.cs:173`

So the concern is not that pipeline missions are never pre-created. They are.

### Vessel and captain context are generated into `CLAUDE.md`

The code does include vessel and captain context in generated mission instructions:

- `ProjectContext`: `src/Armada.Core/Services/MissionService.cs:532`, `:553`
- `StyleGuide`: `src/Armada.Core/Services/MissionService.cs:533`, `:559`
- `ModelContext`: `src/Armada.Core/Services/MissionService.cs:534`, `:565`
- `CaptainInstructions`: `src/Armada.Core/Services/MissionService.cs:537`, `:546`
- persona prompt injection: `src/Armada.Core/Services/MissionService.cs:570`, `:572`

So the problem is not complete absence of context. The problem is that the launch path and the file-based instruction path are not aligned.

## Findings

### 1. Critical: clean process exits can be lost before PID mapping is recorded

Evidence:

- runtime exit event is subscribed before process start: `src/Armada.Server/AgentLifecycleHandler.cs:153`
- process is actually started inside runtime before mapping is stored: `src/Armada.Server/AgentLifecycleHandler.cs:211`
- PID mapping is only written after `StartAsync` returns: `src/Armada.Server/AgentLifecycleHandler.cs:218`
- exit callback fires from `BaseAgentRuntime`: `src/Armada.Runtimes/BaseAgentRuntime.cs:166`
- if no mapping exists, exit handling returns early: `src/Armada.Server/AgentLifecycleHandler.cs:328`, `:334`

Failure mode:

- a fast-failing or fast-finishing process can exit before `_ProcessToCaptain` / `_ProcessToMission` are populated
- `HandleAgentProcessExited` then logs "no captain/mission mapping found" and drops the exit
- later health checks see a missing process and treat it as crash/recovery territory rather than a confirmed completion

This directly matches the symptoms you described: missions stall, repeat, or go into retry loops even though the agent already exited.

### 2. Critical: mission completion/finalization is not single-owner and is race-prone

Evidence:

- `MissionService.HandleCompletionAsync` performs handoff, calls landing, reclaims dock, then releases captain:
  - `src/Armada.Core/Services/MissionService.cs:327`
  - `src/Armada.Core/Services/MissionService.cs:423`
  - `src/Armada.Core/Services/MissionService.cs:433`
  - `src/Armada.Core/Services/MissionService.cs:447`
  - `src/Armada.Core/Services/MissionService.cs:461`
- `MissionLandingHandler.HandleMissionCompleteAsync` also reclaims the same dock:
  - `src/Armada.Server/MissionLandingHandler.cs:580`
- there is an `_InFlightCompletions` dictionary in `MissionService`, but it is never used:
  - `src/Armada.Core/Services/MissionService.cs:41`

Impact:

- dock reclaim is invoked from both `MissionService` and `MissionLandingHandler`
- completion can be triggered from both process exit handling and health check/orphan recovery paths
- there is no real in-flight deduplication, only partial status-based idempotency

The current implementation relies on "mostly harmless duplicate calls" rather than enforcing a single completion owner. That is not strong enough for an orchestrator.

### 3. Critical: stage handoff uses two different output sources and they do not agree

Evidence:

- live stdout is accumulated in memory: `src/Armada.Server/AgentLifecycleHandler.cs:268`, `:269`
- `MissionService` persists that value to `mission.AgentOutput`: `src/Armada.Core/Services/MissionService.cs:412`, `:414`, `:417`
- architect parsing uses `AgentOutput`: `src/Armada.Core/Services/MissionService.cs:1071`-`:1074`
- generic downstream handoff does not use `AgentOutput`; it rereads the mission log file and heuristically strips the preamble:
  - `src/Armada.Core/Services/MissionService.cs:969`
  - `src/Armada.Core/Services/MissionService.cs:996`
  - `src/Armada.Core/Services/MissionService.cs:998`
  - `src/Armada.Core/Services/MissionService.cs:1004`
  - `src/Armada.Core/Services/MissionService.cs:1010`

Impact:

- architect missions and non-architect stages do not consume the same recorded output
- one source is transient in-memory stdout; the other is a reparsed log file
- handoff correctness depends on brittle log formatting assumptions rather than the canonical persisted mission output

This is the main reason output propagation is not reliable today.

### 4. High: stderr is logged but never treated as mission output, heartbeat, or progress

Evidence:

- runtime redirects stderr: `src/Armada.Runtimes/BaseAgentRuntime.cs:87`
- stderr is only written to log with a prefix: `src/Armada.Runtimes/BaseAgentRuntime.cs:143`, `:147`, `:148`
- only stdout triggers `OnOutputReceived`, which feeds heartbeat/progress/output capture

Impact:

- if a runtime or wrapper emits useful output, progress markers, or final diagnostics to stderr, Armada ignores it for orchestration
- heartbeat can appear dead even though the agent is actively producing stderr
- handoff output differs depending on whether the agent wrote to stdout or stderr

This is especially risky because CLIs often mix stdout/stderr behavior across versions.

### 5. High: launch prompt does not include vessel/captain context; that context only exists in `CLAUDE.md`

Evidence:

- generated mission file includes project/style/model/captain context:
  - `src/Armada.Core/Services/MissionService.cs:532`-`:572`
- launch prompt only renders `MissionTitle` and `MissionDescription`:
  - `src/Armada.Server/AgentLifecycleHandler.cs:180`
  - `src/Armada.Server/AgentLifecycleHandler.cs:185`
  - `src/Armada.Server/AgentLifecycleHandler.cs:186`
  - `src/Armada.Server/AgentLifecycleHandler.cs:188`
  - `src/Armada.Server/AgentLifecycleHandler.cs:189`
  - `src/Armada.Server/AgentLifecycleHandler.cs:193`

Impact:

- the system assumes the runtime will discover and honor `CLAUDE.md`
- the direct launch prompt and the persisted mission instructions are not the same source of truth
- archetype-specific context is partially hardcoded in `AgentLifecycleHandler` and partially templated in `MissionService`

This means your statement is basically correct: the archetype is not reliably getting vessel style/model/user context as part of the actual launch prompt.

### 6. High: `TestEngineer` persona prompt resolution is broken in `CLAUDE.md`

Evidence:

- persona template lookup lowercases the raw persona name:
  - `src/Armada.Core/Services/MissionService.cs:629`
  - `src/Armada.Core/Services/MissionService.cs:632`
- built-in template is named `persona.test_engineer`, not `persona.testengineer`:
  - `src/Armada.Core/Services/PromptTemplateService.cs:481`
  - `src/Armada.Core/Services/PromptTemplateService.cs:483`

Impact:

- a mission with persona `TestEngineer` resolves to `persona.testengineer`
- that template does not exist
- the system falls back to the generic prompt instead of the intended test-engineer instructions

This is a concrete defect, not a theory.

### 7. High: process exit handling is duplicated between event callback and health check

Evidence:

- event-driven completion path:
  - `src/Armada.Server/AgentLifecycleHandler.cs:328`
  - `src/Armada.Core/Services/AdmiralService.cs:553`
- health-check completion/recovery path:
  - `src/Armada.Core/Services/AdmiralService.cs:746`
  - `src/Armada.Core/Services/AdmiralService.cs:760`
- missing-PID race suppression only covers one case:
  - `src/Armada.Server/AgentLifecycleHandler.cs:49`
  - `src/Armada.Core/Services/AdmiralService.cs:721`-`:730`

Impact:

- the system has two authorities trying to decide whether a process completed or crashed
- `_HandledProcessExits` only suppresses the case where the PID is already gone and the exit callback already fired
- it does not serialize or deduplicate the full completion pipeline

This is enough to create repeated completions, repeated recoveries, and inconsistent final state depending on timing.

### 8. Medium: orphan recovery passes the wrong captain context into mission completion

Evidence:

- orphan detection explicitly handles the case where `captain.CurrentMissionId != mission.Id`:
  - `src/Armada.Core/Services/AdmiralService.cs:1017`
- it then still calls completion with that captain object:
  - `src/Armada.Core/Services/AdmiralService.cs:1049`

Impact:

- `MissionService.HandleCompletionAsync` uses the passed captain for signals, event attribution, dock fallback, and final release
- in the orphaned case, that captain may already be working on another mission
- completion is therefore using the wrong in-memory captain relationship for the recovered mission

This is logically inconsistent even if it happens to work some of the time.

### 9. Medium: handoff output truncates to the last 8000 chars

Evidence:

- `src/Armada.Core/Services/MissionService.cs:1004`

Impact:

- downstream stages may lose the beginning of the prior output
- for long judge/architect/worker responses, the most important context may be truncated away
- truncating from the front is especially dangerous for structured outputs where the top contains the plan or markers

### 10. Medium: mission log parsing is fragile and format-coupled

Evidence:

- log parsing assumes prompt/output boundaries based on commit trailer lines and exit line formatting:
  - `src/Armada.Core/Services/MissionService.cs:969`-`:1010`

Impact:

- changes to launch prompt format, commit instructions, or runtime log formatting can silently corrupt handoff context
- this logic is not tied to a formal output contract; it is tied to incidental log text

### 11. Medium: completion re-dispatch is local and opportunistic rather than centralized

Evidence:

- after release, `MissionService` immediately picks the first pending mission and tries assignment:
  - `src/Armada.Core/Services/MissionService.cs:463`-`:472`
- `AdmiralService` also has background pending dispatch:
  - `src/Armada.Core/Services/AdmiralService.cs:1060`-`:1088`

Impact:

- dispatch policy is split between background health checks and per-completion opportunistic dispatch
- that makes sequencing harder to reason about under concurrency

This is not necessarily broken by itself, but in combination with the duplicated completion paths it raises risk.

## Critical Path Assessment

### 1. Dispatching a goal

Verdict: partially correct, but not reliable enough.

Confirmed:

- pipeline missions are pre-created
- dependency chains are created
- first-stage auto-assignment exists

Broken or weak:

- archetype context is split across launch prompt and `CLAUDE.md`
- launch prompt lacks vessel style/model/captain context
- `TestEngineer` persona prompt lookup is broken
- persona behavior is duplicated between hardcoded preambles and templates

### 2. Accurate capture of model output

Verdict: not reliable.

Reasons:

- stdout is captured in memory, stderr is not
- persisted `AgentOutput` is not the sole handoff source
- generic handoff reparses logs instead of using canonical stored output
- handoff truncates large outputs

### 3. Accurate capture of model state transitions

Verdict: unsafe under timing races.

Reasons:

- process exit can be missed before PID mapping exists
- health check and exit callback both try to own completion/recovery
- missing real in-flight completion dedupe
- progress and heartbeat ignore stderr

This is the strongest match for your stalled/repeating mission symptom.

### 4. Cleanup and reclamation

Verdict: functionally present but ownership is muddled.

Confirmed:

- captains are usually released after completion
- docks are usually reclaimed

Broken or risky:

- dock reclaim is called from multiple owners
- orphan recovery completes missions with captain state that may already belong to another mission
- cleanup correctness depends on idempotency rather than a single finalizer

## Testing Gaps

Existing tests cover:

- pipeline mission pre-creation
- basic mission completion status transitions
- generation of `CLAUDE.md`
- runtime stdout event emission

But I did not find focused tests for:

- process exits that occur before PID mapping is recorded
- duplicate completion triggers from exit callback and health check
- stage handoff using `AgentOutput` vs mission-log parsing
- stderr-carried progress/heartbeat/output
- `TestEngineer` persona template resolution
- orphan recovery when the captain has already moved on
- duplicate dock reclaim from both mission completion and landing handler

That explains why several of the highest-risk orchestration bugs are still present even though the codebase has decent unit coverage elsewhere.

## Recommended Fix Order

### Immediate

1. Make process registration atomic with launch.
   The PID-to-captain/mission mapping must exist before the process can emit output or exit.

2. Make mission completion single-owner.
   One component should own: persist output, capture diff, handoff, land, reclaim dock, release captain, schedule next work.

3. Stop reparsing mission logs for handoff.
   Persist canonical structured mission output once, then use that exact field everywhere.

4. Fix persona template normalization.
   `TestEngineer` should resolve to `persona.test_engineer` or persona names should be normalized consistently.

### Next

5. Decide whether stderr is part of agent output.
   If yes, capture it consistently for heartbeat/progress/handoff. If not, explicitly document and enforce stdout-only behavior.

6. Unify prompt construction.
   The launch prompt and generated instruction file should come from the same resolved template context, not two parallel systems.

7. Add real in-flight completion dedupe.
   The existing `_InFlightCompletions` field should either be implemented properly or removed.

### After stabilization

8. Add orchestration race tests.
   These need to be deterministic tests around fast exit, duplicate completion, orphan recovery, and repeated dispatch.

## Suggested Concrete Refactor Shape

The cleanest design is:

1. `MissionService` owns mission state transitions and only mission state transitions.
2. `AgentLifecycleHandler` only translates runtime events into structured events.
3. `AdmiralService` owns dispatch policy and recovery policy.
4. `MissionLandingHandler` owns landing only, not reclaim/release.
5. A single completion coordinator owns:
   - capture canonical output
   - persist diff
   - prepare next-stage context
   - invoke landing
   - reclaim dock
   - release captain
   - trigger next dispatch

Right now those responsibilities are spread across all four components.

## Verification Notes

I ran:

- `dotnet test test/Armada.Test.Unit/Test.Unit.csproj --no-restore`

That completed without reporting test failures in the console output, although this project appears to use a custom runner and the output was build-heavy rather than a normal xUnit/NUnit summary.

I also ran:

- `dotnet test test/Armada.Test.Runtimes/Armada.Test.Runtimes.csproj --no-restore`

That run was blocked by a build lock on `src/Armada.Core/obj/Debug/net8.0/Armada.Core.dll`, so I did not get a clean runtime-suite result.

## Bottom Line

Your suspicion is directionally correct.

The dispatch pipeline exists, but the parts that matter for reliability are not robust enough:

- context delivery is inconsistent
- output handoff is not canonical
- process exit handling has a real race that can drop exits
- cleanup/finalization responsibilities are split across multiple owners

If you fix only one thing first, fix the launch/exit registration race and make completion single-owner. That is the shortest path to stopping the voyage stalls and repeat loops.
