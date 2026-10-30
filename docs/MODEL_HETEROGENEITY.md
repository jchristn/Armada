# Model Heterogeneity

Status: agreed plan, 2026-10-26. The open questions were answered by the administrator (see
[Decisions](#decisions)), and work has started on Phase 1 under v1.1.0. Field names and thresholds can still change
as the work lands.

How to make Armada pipelines work when their stages run on different models and runtimes, for example Claude
planning, Qwen or gpt-oss on Mux doing the work, and Codex judging.

Related documents:

- [MODEL_GUIDANCE.md](MODEL_GUIDANCE.md): which traits each persona needs, and how to evaluate a model by hand.
- [HYBRID_DEVELOPMENT_RESULTS.md](HYBRID_DEVELOPMENT_RESULTS.md): measured runs of gpt-oss:20b, gpt-oss:120b,
  qwen3-coder:30b and qwen3.8:27b in FullPipeline.
- [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md): persona captain priorities, tiers and escalation.

## Contents

- [The problem](#the-problem)
- [Part 1: Challenges](#part-1-challenges)
- [Where Armada stands today](#where-armada-stands-today)
- [Design principles](#design-principles)
- [Part 2: The fixture battery and Evaluate](#part-2-the-fixture-battery-and-evaluate)
- [Part 3: Reader-aware handoffs](#part-3-reader-aware-handoffs)
- [Part 4: Feedback to the administrator](#part-4-feedback-to-the-administrator)
- [The model profile](#the-model-profile)
- [Recommended order of work](#recommended-order-of-work)
- [Decisions](#decisions)
- [Decision log](#decision-log)

## The problem

When every stage of a pipeline runs on Claude, pipelines work well. Claude writes instructions that Claude can
follow, and its handoffs suit the next Claude stage. Once open models are mixed in (Qwen, DeepSeek, gpt-oss and
others), stages finish incorrectly, later stages find the problems and send the work back, sometimes to a different
model, and the voyage usually ends up running on Claude anyway. The work lands, but it takes longer and uses many
more tokens than it should.

One contributing cause, to be measured rather than assumed: **Armada builds every prompt and every handoff as if the
reader were Claude, and the stage that writes a handoff does not know who will read it.** The trial evidence mixes
this with model, runtime and orchestration failures, and does not yet show which of them dominates.

Desired end state:

1. An administrator defines a captain (runtime, model, endpoint) and clicks **Evaluate**.
2. Armada runs a fixed, versioned test battery against it through real dispatch, and an evaluator captain they
   choose grades only the subjective parts. The result is an advisory **model profile**: hard capabilities, measured
   outcomes with counts and provenance, and how to talk to the model.
3. When that captain is used in a persona, Armada renders the prompts and handoffs it receives for it.
4. Armada keeps measuring the model in production and tells the administrator when it keeps falling short.

How this document maps to the original request:

| Request | Where it is covered |
|---|---|
| 1. The challenges and difficulties | [Part 1](#part-1-challenges), with today's code in [Where Armada stands today](#where-armada-stands-today) |
| 2. A smoke test that finds a model's strengths, weaknesses and limits (such as context size) | [Part 2](#part-2-the-fixture-battery-and-evaluate) and [The model profile](#the-model-profile) |
| 3. Handing work to a model with knowledge of the next model's capabilities and limits | [Part 3](#part-3-reader-aware-handoffs) |
| 4. Telling the administrator when a model keeps failing or falling short in reviews | [Part 4](#part-4-feedback-to-the-administrator) |
| End state: define a captain, click Evaluate, an evaluator of your choice builds the profile, and everything that hands it work uses the profile | Desired end state above; built in [Phases 2 to 6](#recommended-order-of-work) |

### Evidence and its limits

The recent failed and redeployed missions had already been purged from Armada, so this draft draws on
[HYBRID_DEVELOPMENT_RESULTS.md](HYBRID_DEVELOPMENT_RESULTS.md), the post-mortems in `DECISIONS.md`, and Armada's
source code. Treat that evidence with care:

- It is **one task** (Timestamps) on one vessel, with four models and two or three runs each.
- Every run used server build 222d6ae7, which **predates the stage check that now runs on every runtime by default**
  (commit b0f590bc3). How much waste remains on the current build is unknown.
- Many of the dominant failures were **tooling**, not model quality: Mux tool-call parse 500s (fixed in Mux 1.4.1),
  the Mux editor dropping the UTF-8 BOM, a repository that tracked a generated file, and a Codex sandbox that could
  not run `dotnet`.

So the first step is to fix the clear defects, capture provenance, and rerun the baseline on the current build
before sizing anything else.

## Part 1: Challenges

Grouped by where the failure comes from. Each one makes heterogeneous pipelines fail in a different way, so each
needs a different fix. These are hypotheses drawn from the trial and the code; the battery and the scorecard are
how to find out which ones matter.

### A. The model itself

| # | Challenge | How it shows up |
|---|---|---|
| A1 | **Different strengths.** Some models reason well, some code well, some do both, and few are good at strict output formats. | A good coder writes a weak plan; a good planner writes code that does not compile. |
| A2 | **Context size.** The usable window is often much smaller than the model card says (Ollama `num_ctx`, proxy limits), and quality drops well before the window is full. | Instructions at the start of the prompt are forgotten; the model loses track of files it read earlier; compaction drops the task. |
| A3 | **How much instruction it can follow.** Claude ranks many layered rules ("CRITICAL", "OVERRIDE", nested exceptions). Smaller models follow the last or loudest rule, or skip the middle. | Some rules are followed and others are not, seemingly at random; ASCII, commit and scope rules are broken. |
| A4 | **Tool use.** Some models have no tools; others have tools but emit malformed calls, loop, ignore tool errors, or paste `read_file` line numbers into code. Models also differ in how much tool guidance they need. | Ollama 500s, repetition loops, iteration-limit hits, edits that corrupt files; tools are left unused or overused. |
| A5 | **Writing handoffs for a weaker reader.** A strong model writes handoffs that a strong model can follow. Asked to write for a weaker one, it still writes dense prose that assumes the reader will infer things. | The Worker does part of the task, misses the second of two coordinated edits, or follows the example rather than the rule. |
| A6 | **Speed.** A slow model is not a bad model, but it trips timers that assume progress. | Stall detection, inference request timeouts and proxy timeouts fire on work that would have succeeded. |

### B. The protocol (the work was fine, the report was not)

| # | Challenge | How it shows up |
|---|---|---|
| B1 | **Output contract failures look like work failures.** `[ARMADA:RESULT]`, `[ARMADA:VERDICT]`, required `##` sections, the `armada-plan` block. | Good work is redone because a marker was missing or paraphrased. |
| B2 | **Commit and repository hygiene.** The model does not commit, leaves scratch files, changes encodings or line endings. | Historically, the leftover-work auto-commit picked up `README.md.backup`; this is now prevented for untracked scratch files by `GitService.CommitAllAsync`. The Judge rejects BOM and CRLF changes. |

### C. The prompts and handoffs Armada builds

| # | Challenge | How it shows up |
|---|---|---|
| C1 | **One prompt for every reader.** The mission instructions are long and layered: captain instructions, mission instructions, rules, context conservation, merge-conflict rules, progress signals, papercuts, model context. A 27B model with a 32K window gets the same text as Claude. | The task is buried under boilerplate; small models may give it less attention than the rules around it. Whether a leaner prompt also helps Claude is untested. |
| C2 | **Handoffs are excerpts, cut to length on two paths.** `PipelineHandoffContext` passes the previous stage's output cut to 3000 characters (first and last 1500); `MissionPromptBuilder` separately caps the previous stage's diff at 4000 characters. Both tell the reader to fetch the rest with `mission_status` or `get_mission_diff`. | A reader without MCP tools cannot fetch it that way; the middle of a review (often the findings) is lost; nothing says what was dropped. |
| C3 | **The writer does not know the reader.** The Architect writes slice descriptions before the Worker's captain is chosen, and the Judge writes findings without knowing who will do the rework. | Handoffs are tuned for the writer's own abilities. |
| C4 | **Escalation repeats the same prompt.** When rework escalates to a different captain, the new captain gets the same task spec plus the review, not a prompt that says what already failed. | The same work is paid for twice, and the stronger model has to work out again what went wrong. |
| C5 | **Dense compound instructions.** For example, the rework instruction in `MissionService.Rework.cs` is one paragraph of about 150 words with eight separate demands. | Claude handles it; a small model does one or two of the eight. |

### D. The runtime and environment (not the model's fault, but blamed on it)

| # | Challenge | How it shows up |
|---|---|---|
| D1 | **Runtime differences.** Tool-call format, how errors are returned, and whether malformed calls are retried differ between Mux, OpenCode, pi, the API endpoint runtime and vendor CLIs. | The same model works in one runtime and fails in another. |
| D2 | **Tooling bugs.** The Mux BOM bug, a repository that tracks a generated file, a Codex sandbox that cannot run `dotnet`. | These hit every model but count against whichever model was running. |
| D3 | **Orchestration bugs.** A duplicate relaunch on the same dock, a stage-check timeout shorter than the test run, an exit -1 reported as a restart (see `DECISIONS.md`). | Failures are recorded against the model, and its scorecard is wrong. |

### E. The pipeline structure

| # | Challenge | How it shows up |
|---|---|---|
| E1 | **A skipped check looks like a passed check.** Stage checks run on every runtime by default (`VerifyAllStageRuntimes`), but `VerifyStageWorkAsync` returns `null` both when the checks passed and when they were skipped: for the Judge, Architect, Product Manager and Recorder personas, a Verifier that reported a blocking defect, a missing dock or vessel, no workflow profile or no configured commands, and a vendor-runtime stage that left HEAD unchanged. | Nobody can tell which stages were actually verified; a broken build can still pass through a stage that was silently skipped. |
| E2 | **Fallback discards what was learned.** A failure moves to the next captain without a record of which model failed at what. | The same model fails the same way next week. |
| E3 | **Nobody sees the pattern.** Failures are visible one mission at a time; nothing adds up "qwen3-coder fails the Worker persona 40% of the time on this vessel". | The administrator learns from frustration, not data. |

## Where Armada stands today

Most of the pieces this needs already exist. This is what the design builds on, and the gaps.

| Area | What exists | Gap |
|---|---|---|
| Captain identity | `Captain.Runtime`, `Model`, `Tier`, `ReasoningEffort`, `SystemInstructions`, `AllowedPersonas` | No measured outcomes. `Tier` is inferred from the model name by regex (`CaptainTierSelector`) unless set by hand. |
| Endpoint settings | `InferenceOptions.ContextLength`, `ToolsEnabled`, `McpToolsEnabled`, `MaxIterations`; Mux `ContextWindow`; `ApiContextCompactor` | Known to the runtime, not to the prompt builder or to upstream stages. |
| Endpoint probing | `validate_model_endpoint`, `health_check_model_endpoints`, `ModelEndpointProbeResult` (latency, status, sample text) | It checks that the endpoint responds, not what the model can do. |
| Stage checks | `VerifyStageWorkAsync` runs the workflow profile's build and test commands after a stage; `VerifyAllStageRuntimes` defaults to `true` | Returns `null` for both "passed" and every skip reason, so results cannot be reported as passed, failed, skipped or could not run. |
| Prompt assembly | `MissionPromptBuilder`, prompt templates resolved per persona with project-profile overrides (`ResolvePersonaPromptAsync`), persona briefs, `CaptainInstructions` (up to 800 characters) | One template set for every reader. The only per-captain variation is the captain's free-text instructions. |
| Handoff | `PipelineHandoffContext.Build` (3000-character output excerpt plus mission and branch references); a 4000-character previous-stage diff cap in `MissionPromptBuilder`; Architect `armada-plan` JSON with requirements, decisions, acceptance criteria and validation commands | The plan is structured, but each mission's `Description` is free text, and later stages hand off excerpts with no record of what was dropped. |
| Protocol recovery | `TryScheduleOutputRepairAsync` reruns a stage with the exact format contract for the Architect, the Judge and the Verifier contracts; `MissionFailureKindEnum.InvalidOutput` | No repair for other personas (for example the Worker's commit and completion line); repairs are not counted per model; the repair rules on tools and evidence are not stated. |
| Failure taxonomy | `MissionFailureKindEnum`: Compile, TestFail, Timeout, NoOp, ScopeViolation, JudgeRejected, Infra, InvalidOutput, and others | A good base. Not combined per model and persona, and no suspected cause. |
| Rework and escalation | Judge rework rounds, stagnation detection, `CaptainEscalationSelector` | The escalated captain gets the same prompt; the failed model is not recorded anywhere that drives later routing. |
| Quarantine | `Captain.QuarantineUntilUtc`, `QuarantineReason` | Used for configuration failures, not for poor quality. |
| Data | Token usage records, mission events, Judge verdicts, papercuts | Everything a scorecard needs is recorded but not combined, and attempts do not record runtime version, sampling or prompt version. |

## Design principles

1. **Measure before adapting.** No rendering change, profile setting or routing rule goes in without a repeatable
   measurement on the fixture battery. The battery comes before the adaptive machinery, not after it.

2. **A stable profile identity with immutable configuration revisions.** A profile belongs to a model, not to a
   captain or a Harbor: the administrator sees one profile per (runtime, provider, model, served context), shared by
   every captain that runs that model on any Harbor. Facts about one Harbor (whether it can run `dotnet`, file access)
   stay on the captain. Each attempt records the exact configuration it ran with: runtime
   version, model digest when available, sampling settings, context, tool configuration, prompt version and battery
   version. Results are aggregated across revisions only when they are explicitly compatible, so a runtime fix is
   neither hidden by pooled data nor turned into a new profile for every patch release.

3. **Do not rely on the upstream model to write for the downstream model.** Asking Claude to "write this for a weak
   32K model" is unreliable and cannot be checked. Instead:
   - upstream stages write **structured content** that does not depend on the reader (steps, files, acceptance
     criteria, commands, findings), which Armada already partly has in the `armada-plan`;
   - Armada **renders** that content into a prompt for the reader at launch time, when the captain is known,
     deterministically and testably;
   - an LLM rewrite of the prompt is considered only if measurements show rendering is not enough.

4. **Separate "the work was wrong" from "the report was wrong", without changing what a verdict means.** A missing
   marker or section can be repaired cheaply, and repairs are counted on their own line of the scorecard. A repair:
   - is bounded in attempts;
   - keeps both the original and the repaired output;
   - runs without executable tools where possible, because it is a formatting task;
   - never creates a PASS, test evidence or findings that the original output did not contain;
   - fails the stage as invalid output when attempts are exhausted or the result is ambiguous.

   A missing commit is not a formatting problem; it needs an inspection of the repository.

5. **Check mechanically first, and report what was checked.** Build, test, diff scope, encoding and marker checks run
   after every work stage. Each result is recorded as passed, failed, skipped (with the reason) or could not run. A
   missing failure is not evidence of verification. LLM review is the last check, not the first.

6. **A fixed evaluation battery; the evaluator only grades.** If the evaluator captain invents tests each time, two
   profiles cannot be compared. The battery is fixed and versioned; the evaluator grades the subjective parts and
   writes the summary.

7. **Probes produce observations, not capacities.** Passing twelve constraints in a synthetic prompt does not mean a
   model can follow twelve rules in a mission; retrieving a needle does not establish a usable context. Configured
   limits are kept separate from task-specific measured results, and no synthetic competence score is computed.

8. **Production data beats the evaluation.** The evaluation gives a starting picture. Results from real missions are
   shown beside it, and a gap between the two is flagged.

9. **Do not blame the model for the environment.** Infra, tooling and orchestration failures (D2, D3) are left out of
   the model's results. Where the cause is unclear, it is recorded as unknown or mixed, not guessed.

## Part 2: The fixture battery and Evaluate

The battery is built first as a regression harness for Armada itself (Phase 2), and Evaluate later reuses it to
assess a new captain (Phase 5). The same tasks measure a rendering change and a new model.

### The fixture battery

The battery is organized as **packs**. A pack is one fixture repository plus its tasks, reset before each task, and
the administrator chooses which packs to run. The first packs are one per language: C#, JavaScript, Python, Java,
Rust, Go, PHP and Ruby. The C# pack is built first; the others follow the same layout.

Packs are data, not code in Armada: a manifest that names the fixture, the tasks, the persona each task tests, how it
is graded (a command such as build or tests, a byte comparison, a seeded-set match, or the evaluator's rubric) and the
toolchain the Harbor needs. Adding a language, or a domain that is not code at all (documentation, data work,
operations runbooks), means adding a pack, not changing Armada. A pack whose toolchain the Harbor lacks is reported as
could not run, never as failed.

Every task runs through real dispatch, Harbor execution, stage checks and completion parsing, so the result reflects
Armada and not only the model. The tasks below are the template each code pack implements:

| Task | Persona it tests | Graded by |
|---|---|---|
| Add a member to an existing class | Worker (easy) | Build and tests |
| Coordinated two-place edit (write a test method and register it) | Worker (the step gpt-oss failed) | Build and tests |
| Fix a bug from a failing test | Worker | Tests |
| Edit a file with a BOM and CRLF line endings | Worker | Byte comparison: both are kept |
| Write tests for an untested method, including negative paths | Test Engineer | Tests plus mutation check |
| Review a diff with three seeded defects (negative control) | Judge | Findings matched against the seeded set; false positives count |
| Review a correct diff (positive control) | Judge | A PASS with no invented blocking findings |
| Turn a feature request into an `armada-plan` | Architect | Schema check, then the evaluator grades it |
| Lint a diff with seeded style violations | Linter | Matched against the seeded set |
| Summarize a voyage | Recorder | Evaluator grades it |

Seeds, sampling settings, run count (default 3), the battery version and the full configuration revision are
recorded on every run. Battery missions are tagged so they never affect production scorecards.

### Mechanical probes (Evaluate, Quick)

Direct calls through the captain's runtime, using the same tool loop the captain uses in production. Each records
an observation, not a capacity.

| Probe | Method | Records |
|---|---|---|
| Served context | Confirm the context the endpoint actually serves against the configured value | Served context; a mismatch is a configuration error |
| Output contract | Ask for Armada's real markers and sections (`[ARMADA:RESULT]`, `[ARMADA:VERDICT]`, `armada-plan` JSON) | Exact compliance; how often a repair is needed |
| ASCII and format rules | Tasks that tempt em dashes, smart quotes and ANSI codes | Violation count |
| Tool calling | Single, sequential and parallel calls; a tool that returns an error; a tool with a large result | Malformed calls; recovery from errors; looping |
| Speed | Time to first token and output tokens per second at 4K and 32K input | Advice for inference request, inactivity and stall timeouts only |
| Determinism | The same short task 3 times | Variance |

How much tool guidance a model needs (bare tool definitions, brief guidance, or step-by-step instructions) is not
probed yet. It is added as a probe and a rendering setting only when the battery shows the difference matters.

Model speed does not set stage-check timeouts. Stage checks run build and test commands, so their timeout follows the
measured duration of those commands on the Harbor.

Further competence probes (instruction capacity at N constraints, handoff writing, handoff reading styles) are not
part of the plan. They are research questions; add one only if it is shown to predict production results on the
battery and in the scorecard.

### Evaluate (Full)

On a captain (or a model endpoint), **Evaluate** opens a dialog:

- evaluator captain for the subjective grading (default: the highest-tier idle captain of a different model family
  from the candidate; a same-family evaluator is allowed only when no other is idle, and the report carries a
  warning);
- depth: **Quick** (mechanical probes only, the default) or **Full** (adds the fixture battery; only when asked for);
- packs to run (default: the packs matching the languages of the administrator's vessels);
- personas to score (default: all the captain is allowed).

The result is an advisory profile update and a report:

- per-persona outcomes with counts, which are the source of truth;
- a short **strengths and weaknesses** section the evaluator writes from those counts only, for example "Strong:
  single-file Worker edits (3 of 3). Weak: coordinated two-place edits (0 of 3); reports often need repair (2 of 3)";
- observed problems and a short "how to work with this model" note.

Nothing changes routing automatically.

Evaluation runs have their own token budget and concurrency limit, separate from production, and never count in
production scorecards. Models that share one machine (for example several local models on one GB10 with 128 GB of
shared memory) are evaluated one at a time: the concurrency limit applies per inference host, not only overall.

### Where it lives in Armada

- A built-in pipeline, `ModelEvaluation`, plus a fixture vessel that Armada can create on request.
- Mechanical probes run as Harbor jobs through the captain's runtime.
- MCP and REST: `evaluate_captain`, `get_model_profile`, `list_model_profiles`.
- Vendor CLI captains (Claude Code, Codex and others) are profiled and evaluated the same way, because the Judge and
  Architect seats vary too.

## Part 3: Reader-aware handoffs

From most to least deterministic. Each change is tested on the battery separately, so the effect of a handoff change
is not confused with the effect of a prompt change.

### 3.1 Structured, versioned handoff artifacts

Replace the 3000-character output excerpt and the 4000-character diff cap with a durable handoff artifact per stage:

- the artifact is versioned and contains the findings, acceptance criteria, check results (passed, failed, skipped,
  could not run) and commit references;
- at launch, Armada delivers to the reader, within a token budget:
  - the unresolved findings and required actions;
  - the relevant diff sections;
  - a pointer to the complete artifact through tools the reader actually has (MCP tools, or a file in the worktree
    for a reader that can only read files);
  - an explicit list of what did not fit.

"No MCP tools" does not mean "inline everything": inlining everything can recreate the context problem.

The rework prompt is built from the artifact (numbered findings, each with file, line and required fix) instead of
one long paragraph.

**Implemented (Phase 3, item 1).** `HandoffArtifact` (schema version 1, table `handoff_artifacts`, migration 97) is
stored once per stage attempt by the stage outcome recorder and linked to the outcome, mission, persona and captain. It is
built from what Armada already parses: protocol lines, the Judge verdict and its `Affected Case` blocks (numbered
findings with file, line, severity and required fix), a verifier's block, the stage-check result, the commit and the
diff (outputs and diffs over 1,000,000 characters are measured and pointed to, not stored). It expires with its mission
through `DataExpiryService`. At launch `HandoffRenderer` replaces the 3000-character output excerpt and the
4000-character diff cap: the budget is one eighth of the reader's served context when its endpoint sets one, otherwise
the `handoffBudgetTokens` setting (default 4000); findings and required actions come first, then checks, diff sections of
files named in findings, other diff sections and the output; then a pointer chosen from the reader's actual tools (MCP
`get_handoff_artifact`, or `.armada/handoff/<persona>-<missionId>.md` in the worktree, git-excluded and a scratch path)
and an explicit did-not-fit list. The existing templates receive the rendered block through `{PreviousStageOutput}` and
`{Diff}` and the prior-stage section of the description; their wording is unchanged. REST
`GET /api/v1/missions/{id}/handoff-artifacts` and `GET /api/v1/handoff-artifacts/{id}`, MCP `get_handoff_artifact` and
`list_handoff_artifacts`, and the .NET client expose the artifacts, tenant-scoped like stage outcomes. The rework prompt
is built from the artifact with one demand per line (item 2, see its Status under Phase 3). The effect of neither item has
been measured on the battery.

### 3.2 Render prompts for the reader (Armada, at launch)

`MissionPromptBuilder` already runs when the captain is known, and `ResolvePersonaPromptAsync` already chooses a
template per persona with project-profile overrides. Rendering uses that path instead of new machinery:

- **First, test a lean default.** Compare today's default against a leaner prompt (one demand per line, the
  contract repeated at the end, less emphasis wording, less boilerplate) on the battery for strong and weak models.
  If the lean version does not hurt strong models, replace the default instead of forking it.
- **Then, at most two tiers: Full and Compact.** The profile selects the tier. Acceptance criteria and project rules
  are kept in both.
- **Rules that need no judgment** come from hard facts: the served context sets the budget, and the reader's actual
  tools decide how the full artifact is offered (3.1).
- A small set of explicit rendering overrides on the profile (tier, contract repeated at the end, maximum files per
  mission) can be edited by hand. Further settings are added only when the battery shows they matter.

### 3.3 Size missions with a Worker eligibility envelope

Predicting which captain will run the Worker stage breaks when a busy captain falls through to the next one, and
sizing every mission for the weakest fallback wastes strong captains. Instead, the Architect slices for an explicit
**Worker eligibility envelope** (for example: at most 3 files and 6 steps, served context at least 32K, tools
required), and dispatch enforces it: only captains whose profile meets the envelope are eligible. Escalation can
select a stronger compatible captain.

Who sets the limits: each vessel has a default envelope, and the Architect may tighten it for one mission (fewer
files or steps) but never loosen it. When no idle captain meets the envelope, the mission escalates to a stronger
compatible captain rather than waiting. Because this is a routing change, it is the one exception to "nothing changes
routing automatically", and it ships last (Phase 6).

The envelope is given to the Architect as a short, generated description, and to the Judge so its findings are
actionable by the rework reader.

### 3.4 Escalation with memory

When work escalates to a different captain:

- the prompt says what was attempted, by which model, and what failed (findings and check output), rendered for the
  new reader from the handoff artifact;
- the escalation is recorded against the failed profile, revision and persona (Part 4);
- the new captain is told, in one clear line, to keep correct parts of the prior work rather than start over.

### 3.5 Translate when the gap is large (deferred)

An LLM "translator" turn that rewrites a slice for a weak reader is not planned. Consider it only if 3.1 to 3.3 do
not close a measured gap.

## Part 4: Feedback to the administrator

### Failure classification

The scorecard is only useful if each failure records *why* the model fell short, not just that it did. Record two
things separately on every failed, reworked or escalated stage:

- the **observed failure**: `MissionFailureKindEnum` (Compile, TestFail, JudgeRejected, InvalidOutput, and so on)
  plus the stage-check status;
- the **suspected cause**, which may be `Unknown` or `Mixed`, with the evidence and the source of the
  classification (mechanical, Judge suggestion, or manual correction).

| Suspected cause | Meaning | Typical signals | Usual remedy |
|---|---|---|---|
| Protocol | The work was acceptable, but the report broke the contract | `InvalidOutput`, missing markers or sections | Contract repair, compact template (not a different model) |
| Instruction following | Some rules or steps were skipped | `ScopeViolation`, `Boundary`, ASCII or commit rules broken, a missed second edit | One demand per line, smaller slices |
| Tool use | The tool loop failed | Malformed calls, loops, iteration limits, `StallRecoveryExhausted` | A different runtime or tool configuration |
| Context | The model lost the task or earlier reads | Long sessions, compaction, forgotten instructions | A smaller budget, smaller slices |
| Capability | The model could not do the work | `Compile`, `TestFail`, `JudgeRejected` with behavior findings after a clean protocol | Move it down or off this persona |
| Not the model | Environment, tooling or orchestration | `Infra`, `OperatorAction`, `Crash` from the runtime, tooling papercuts | Excluded from the model's results |
| Unknown or mixed | The cause cannot be separated mechanically (context loss and insufficient capability often cannot) | Any | Collect more runs before acting |

Mechanical checks assign what they can. The Judge suggests a cause for its rejections, and any cause can be corrected
by hand later. Because past failures were purged, this has to be captured going forward, starting in Phase 1.

A large `Unknown` share at the start is expected and acceptable: it shows what the rules do not yet recognize. So the
classification is built to be amended:

- the rules that map signals (failure kind, stage-check status, finding categories, runtime errors, log patterns) to a
  suspected cause are data the administrator can view, add to and edit, not hard-coded;
- built-in rules ship as defaults that can be overridden, and every classification records which rule (or which
  person) produced it;
- a triage view lists `Unknown` and `Mixed` stages so they can be classified by hand, and a hand classification can be
  turned into a new rule;
- a changed rule can be re-applied to past stages, so the scorecard improves without new runs.

### Escalation record

Every escalation records: which profile and revision failed, in which persona, at what (observed failure and
suspected cause), which captain finished the work, and whether that captain kept or replaced the prior work.

In the stage outcomes this is the escalated attempt's outcome (`Disposition` `Escalated`): its captain, runtime and
model, persona, observed failure and suspected cause, the captain the work went to (`NextCaptainId`) and the commit it
left on the branch (`CommitHash`). When the mission completes, Armada links it to the finishing attempt
(`FinishedByOutcomeId`, `FinishedByCaptainId`) and sets `PriorWorkKept`: true when the escalated commit is an ancestor
of the finished commit, false when it is not (the work was replaced), null when either commit or the repository could
not be read. Keeping is judged by ancestry only: a captain that keeps the history but reverts the changes counts as kept.

### The scorecard

One scorecard per (profile, persona), optionally per vessel, over a rolling window (default: last 30 days or last 50
missions). Every rate is shown with its count ("4 of 11"), and revisions are shown separately unless marked
compatible.

| Metric | Source |
|---|---|
| First-pass success (stage checks passed, not skipped, and no rework needed) | Stage check results, rework rounds |
| Stage-check status counts: passed, failed, skipped (by reason), could not run | Stage check results |
| Judge verdicts on its work (PASS, NEEDS_REVISION, FAIL) and the categories of findings | Judge missions; findings tagged as behavior, scope, style or hygiene |
| Protocol repairs, and repairs that ran out | Output repair attempts, `InvalidOutput` |
| Tool failures (malformed calls, loops, iteration limits) | Runtime failure classification, Mux run outcomes |
| Resumes in place and stalls | Recovery events |
| Escalations away from this model, and whether the next model succeeded | Escalation records |
| Total cost and time per accepted result, including rework and fallback | Token usage and timing across all attempts that led to the accepted result |

Cost is reported per accepted result rather than as "wasted" tokens, because later work often keeps useful parts of
an earlier attempt.

**Attribution rules:**

- Failures classified as `Infra`, `OperatorAction`, tooling papercuts or orchestration bugs are excluded.
- A defect is charged to the stage that introduced it, including the Test Engineer, Usability Engineer or Linter, not
  automatically to the Worker.
- A Judge rejection counts against the Worker's profile only if the finding is about the Worker's slice.
- A later defect counts against the **Judge's** profile only when the commit the Judge reviewed contained the defect
  and the defect was within the review's scope.
- Unknown and mixed causes are shown, not forced into a category.
- Evaluation and battery missions are excluded.

### Alerts

Thresholds per persona, with defaults and per-persona overrides, each with a minimum count, for example:

- first-pass success below 50% over at least 8 missions;
- 3 escalations away from the same profile and persona in a row;
- protocol repair rate above 30% (suggests a compact template, not a different model);
- cost per accepted result well above the persona's median.

When a threshold is crossed:

1. An inbox item and an optional push notification: "qwen3-coder:30b as Worker on Armada: first pass 4 of 11, 6
   escalations to Claude. Most common finding: missing second edit in coordinated changes."
2. Suggested actions with one-click apply: move it down in the persona's priorities, remove it from the persona,
   switch it to the compact template, re-evaluate, or ignore for N days.
3. An optional automatic mode (off by default): a **persona-scoped quarantine**. The captain stays available for
   other personas but is skipped for this one until re-evaluated or released. This extends today's quarantine,
   which applies to the whole captain and only for configuration errors.

### Closing the loop

- The scorecard is shown beside the evaluation results, and a large gap is flagged ("evaluated: 3 of 3 Worker tasks;
  observed: 5 of 11; re-evaluate").
- A finding category that recurs for a profile can be turned into a rendering override by the administrator (for
  example, a checklist line for coordinated edits), and the effect is checked on the battery.

## The model profile

A first sketch. Field names are up for discussion. Profile ids use the `mdl_` prefix and revision ids `mdv_` (`mpr_` is
taken by model probe runs). The profile is advisory: it informs rendering, the eligibility
envelope and the administrator; it does not set `Captain.Tier` or change routing automatically.

```json
{
  "id": "mdl_...",
  "identity": {
    "runtime": "Mux",
    "provider": "Ollama",
    "model": "qwen3.8:27b",
    "servedContextTokens": 131072
  },
  "revisions": [
    {
      "id": "mdv_...",
      "runtimeVersion": "1.4.1",
      "modelDigest": "sha256:...",
      "sampling": { "temperature": 0.1 },
      "toolConfiguration": { "tools": true, "mcp": false },
      "compatibleWith": [],
      "firstSeenUtc": "2026-10-26T00:00:00Z"
    }
  ],
  "capabilities": {
    "toolsAvailable": true,
    "mcpAvailable": false
  },
  "rendering": {
    "template": "Compact",
    "repeatContractAtEnd": true,
    "maxFilesPerMission": 3
  },
  "measured": [
    {
      "revisionId": "mdv_...",
      "source": "battery",
      "batteryVersion": 1,
      "promptVersion": "...",
      "persona": "Worker",
      "task": "coordinated-two-place-edit",
      "passed": 2,
      "runs": 3,
      "repairs": 1,
      "costPerAcceptedResultTokens": 410000
    }
  ],
  "notes": "Slow but careful. Checks its own work. Keep each mission to one objective."
}
```

Notes:

- `capabilities` describes what the model can use as served by this runtime and provider, not what the model card
  says. What a captain can do on its Harbor (read worktree files, run build and test commands) is recorded on the
  captain, because the same model can run on several Harbors.
- **Pooling revisions:** a new runtime major version, a new model digest, or a change in served context or sampling
  starts a new revision that is not pooled with earlier ones by default. A runtime patch release is pooled
  automatically. The administrator can override either way, and the scorecard can suggest pooling when two revisions
  show the same results.
- `rendering` holds explicit overrides only. Manual values always win; measured results are shown beside them as
  advice.
- `measured` holds counts with provenance, not synthetic scores. Speed observations feed inference and inactivity
  timeout advice only.

## Recommended order of work

Measure first, then change handoffs and contracts, then prompts, then add profiles, Evaluate and routing.

### Phase 1: correct, fix and measure the baseline

1. Correct stale claims (this revision does so for this document).
2. Fix the clear orchestration defects in `DECISIONS.md` (duplicate relaunch, stage-check timeout, exit -1
   misclassification) and skip scratch files in the leftover-work auto-commit. Addresses D2 and D3. The first three
   landed before this plan (commits 31caab7d6 and 7b4bfa09f). Scratch-file skip: done (B2) in
   `GitService.CommitAllAsync`, called by `MissionService.CommitLeftoverWorkAsync`, using
   `ScratchFileMatcher.IsScratchFile`. Untracked files with case-insensitive suffixes `.backup`, `.bak`, `.orig`,
   `.rej`, `.tmp`, `.swp`, `.swo`, or `~`, names `.DS_Store` or `Thumbs.db`, or paths under `.armada/handoff/`
   are left untracked in the worktree, not deleted. Other `.armada/` paths are not blanket-excluded by this matcher;
   normal git ignore rules still apply. Tracked files with scratch-like names remain eligible for commit.
   Existing `Services.GitService` regressions `commit_all_leaves_untracked_scratch_files_out`,
   `commit_all_with_only_scratch_files_commits_nothing`, and `commit_all_keeps_tracked_files_with_scratch_names`
   cover real work beside `README.md.backup` and `.orig`, scratch-only leftovers, and tracked-file preservation.
3. Change `VerifyStageWorkAsync` to return a result that distinguishes passed, failed, skipped (with the reason) and
   could not run, and record it on the mission. Addresses E1.
   Status: done through `CheckStageWorkAsync` returning `StageCheckResult` (the legacy `VerifyStageWorkAsync`
   remains a failure-message wrapper). `AdmiralService.RecordStageCheckAsync` records the result in a
   mission-linked `mission.stage_check` event. `Services.HandoffResilience` covers passed, failed, skipped
   with reasons (`stage_check_result_says_why_a_frontier_stage_was_skipped`) and could-not-run event
   round trips (`stage_check_event_round_trips_the_result`); `Services.StageCheckResumeSafety`
   `timeout_check_never_completes_without_landing` asserts a recorded CouldNotRun timeout.
4. Record attempt-level provenance: runtime version, model digest, sampling, context, tool configuration, prompt
   version, and the escalation record. Record the observed failure and suspected cause on every failed, reworked or
   escalated stage.
   Status: done in `StageOutcomeService.RecordAttemptAsync`, storing attempt-level `StageOutcome` provenance,
   observed failure, suspected cause and escalation links. `Services.StageOutcomes`
   `record_attempt_provenance_and_classification` and `provenance_recorded_and_cached_per_captain_and_harbor`
   cover persistence and runtime/model probes; unavailable provenance is recorded as null.
5. Start the failure classification (observed failure, suspected cause, amendable rules) so data accumulates while
   the rest is built.
   Status: done through `FailureCauseClassifier`, persisted amendable rules and `classify_stage_outcome` /
   `reclassify_stage_outcomes`. `Services.StageOutcomes` `manual_classification_survives_reclassify` and
   `reclassify_applies_new_rule_to_past_outcomes`, plus `E2E.StageOutcomes` `mcp_tools`, cover this.
6. Rerun the Timestamps baseline from HYBRID_DEVELOPMENT_RESULTS.md on the current build, with the same models
   (gpt-oss:20b, gpt-oss:120b, qwen3-coder:30b, qwen3.8:27b), 2 to 3 runs each, and publish it next to the old
   results. The four models share one GB10, so the runs go one at a time.

### Phase 2: Quick probes and the fixture battery

1. Quick probes (served context, output contract, ASCII and format rules, tool calling, speed, determinism) as a
   first, probe-only **Evaluate**. They need nothing else to exist and catch context mismatches at once.
2. The pack format and the C# pack with the tasks in [The fixture battery](#the-fixture-battery), running through
   real dispatch, Harbor execution, stage checks and completion parsing, with positive and negative review controls.
   The other language packs follow.
3. Record a baseline for each model in use.

### Phase 3: handoffs and contract repair

1. Durable, versioned handoff artifacts with budgeted delivery suited to the reader (3.1), replacing the
   3000-character output and 4000-character diff excerpts.
2. Rework prompts built from the artifact, one demand per line. Addresses C2 and C5.
   Status: done. `ReworkPromptBuilder` replaces the one-paragraph rework instruction. When the rework is scheduled, the
   description gets a rework section: a findings placeholder, the demands as a numbered list with one demand per line
   (every demand of the old paragraph is kept), and the output contract. At launch the placeholder becomes the numbered
   findings (severity, file, line and required fix) that `HandoffRenderer` delivered within the reader's budget. Findings
   that did not fit stay in the did-not-fit list next to the pointer to the complete artifact. The rest of the rendered
   handoff follows without its findings section, so the findings are delivered once. The output contract is repeated as
   the last line. A review without structured findings, or a launch without an artifact, keeps the rendered handoff block
   or the fallback line. The section is part of the mission description, so it applies to the Current, Lean and Compact
   variants alike; no template placeholder or migration was added, and non-rework prompts are unchanged. A later rework
   round replaces the earlier round's section instead of stacking it. The effect on the battery has not been measured.
3. Safe contract repair (principle 4) for the remaining personas, starting with the Worker's completion line. A
   missing commit is handled by inspecting the repository. Addresses B1.
   Status: done. A Worker or Test Engineer with a missing or malformed completion line is judged by its dock branch
   (new commits, or changes committed by `GitService.CommitAllAsync`, which skips untracked scratch files as
   described in Phase 1 item 2); with nothing there it stays unfinished work.
   A single Judge verdict in a nearby form and a single valid plan outside an `armada-plan` fence are appended in
   canonical form below the original output; ambiguous or duplicated verdicts and two plan blocks are refused. Each
   repair is recorded as a `mission.contract_repaired` event (`ContractRepairRecord` payload with the repair kind),
   and the attempt's stage outcome carries it too: `StageOutcome.ContractRepairKind` (column `contract_repair_kind`,
   migration 98) holds the latest repair recorded since the attempt started, or null when none was needed. When one
   attempt needs two repairs (a leftover-work commit and a completion-line repair) the outcome keeps the last and each
   repair still has its own event.
4. Measure each change on the battery, separately from prompt changes.

### Phase 4: prompts

1. Compare a lean default against Full and Compact variants through the existing persona template-resolution path,
   keeping acceptance criteria and project rules (3.2).
   Status: variants available. Current (today's default, the Full tier), Lean and Compact are built-in templates
   (`persona.<name>`, `persona.<name>.lean`, `persona.<name>.compact`) for the Worker, Test Engineer, Judge and
   Architect; other personas render Current. Selection order: the voyage captain override's `PromptVariant` for the
   persona (exact, then `*`), the captain's `promptVariant` runtime option, then Current. To compare, run the same
   captain, packs, run count and seed once per variant, for example `run_battery` with `promptVariant` Current, Lean
   and Compact; the variant is recorded in each run's configuration (and revision), so compare the runs' counts.
2. If the lean default does not hurt strong models, replace the default instead of forking it.

### Phase 5: advisory profiles, scorecard and Evaluate

1. Profile entity with a stable identity and immutable revisions; captains reference it.
   Status: implemented (migration 107). `ModelProfile` (`mdl_`) keyed by tenant, runtime, provider, model and served
   context; immutable `ModelProfileRevision`s (`mdv_`) with the Decision 6 pooling rules (a served context change is a
   new profile); compatibility marks stored beside the revisions; `Captain.ModelProfileId` set best effort from stage
   outcomes and Quick probe runs; REST `/api/v1/model-profiles` and MCP `get_model_profile` / `list_model_profiles`.
   The `measured` block, the scorecard and Full Evaluate are not part of it.
2. Scorecard with counts, uncertainty, repair frequency and cost per accepted result, using the attribution rules.
   Status: implemented (no migration; computed on request from stage outcomes, token usage and timing).
   `ModelScorecardService` and REST `GET /api/v1/model-profiles/{id}/scorecard`, MCP `get_model_scorecard`: one
   (profile, persona), optionally one vessel, over the last 30 days or 50 missions, whichever is smaller. Rates carry
   their count, denominator and a 95% Wilson interval; revisions are separate groups unless marked compatible; every
   attribution rule reports how many items it removed. Judge finding categories, tool failure kinds and recovered stalls
   are not recorded today and are returned as unavailable. There is no dashboard model profile view yet, so there is no
   scorecard panel, and no fleet-wide listing.
3. Inbox alerts and suggested actions. No automatic changes.
   Status: implemented (migration 108, `model_alerts`). `ModelAlertService` evaluates the scorecard of every (profile,
   persona) with production attempts in the last 30 days (and each vessel with `ModelAlerts.IncludeVessels`) every 60
   minutes (`ModelAlerts.IntervalMinutes`; `Enabled` turns the pass off) and on demand. Defaults (Decision 13), with
   per-persona overrides in `ModelAlerts.Thresholds`: first pass below 50% over at least 8 missions; 3 escalations away
   in a row (a new scorecard metric, `escalations.inARow`); protocol repairs above 30% over at least 8 attempts; tokens
   per accepted result above 2x the median of the other profiles in the persona, at least 5 accepted results on each
   side. A rule whose metric is unavailable is inactive and reported as such. One alert per (profile, revision group,
   persona, vessel or all, rule), kept while the condition holds, resolved when it clears and reopened when it returns;
   it shows in the inbox (kind `model_alert`) for admins, with counts and the Wilson interval in the title, and is pushed
   only with `ModelAlerts.PushEnabled` (off by default). Suggested actions apply only on request, through the persona
   update and Evaluate, and are recorded on the alert: move down in the persona's priorities, remove from the persona,
   re-evaluate (Quick), ignore for N days (default 7). The compact template is listed as unavailable: Armada has no
   prompt variant per profile and persona. REST `/api/v1/model-alerts`, MCP `list_model_alerts`, `get_model_alert`,
   `evaluate_model_alerts`, `get_model_alert_thresholds`, `update_model_alert_thresholds`, `apply_model_alert_action`,
   `ignore_model_alert`. The persona-scoped quarantine stays in Phase 6.
4. Full Evaluate: the fixture battery on top of the Quick probes from Phase 2, with the evaluator grading only the
   subjective parts and writing the strengths and weaknesses summary from the counts.

### Phase 6: sizing and routing

1. Worker eligibility envelope (3.3), enforced by dispatch; escalation selects a stronger compatible captain.
2. Routing changes based on profiles only after held-out production tasks support them.
3. Optional persona-scoped quarantine, off by default.

### Release v1.2.0

The items of Phases 1 to 3 ship in v1.2.0; Phases 4 to 6 come in later releases.

1. Version strings (`Directory.Build.props`, `Constants.ProductVersion`, the Helm csproj, the dashboard and mobile
   packages) read 1.2.0.
2. The Docker compose files, update scripts and docs reference the `v1.2.0` images.
3. CHANGELOG has a v1.2.0 section, and UPGRADING has a v1.1.0 to v1.2.0 section.
4. `build-all.sh v1.2.0` builds and pushes the images.

## Decisions

The open questions of the earlier drafts, as answered by the administrator on 2026-10-26.

| # | Question | Decision |
|---|---|---|
| 1 | Who adapts a handoff to its reader? | Armada renders structured content for the reader at launch; the Architect gets only the short Worker envelope. |
| 2 | How thorough is Evaluate? | Counts are the source of truth. The evaluator adds a short strengths and weaknesses section based only on those counts. |
| 3 | When does Evaluate arrive? | The Quick probes move up to Phase 2 as a probe-only Evaluate; Full Evaluate stays in Phase 5. |
| 4 | Tool guidance level and rule tone? | Deferred until the battery shows they matter. |
| 5 | What is a profile tied to? | The model as served (runtime, provider, model, served context), shared across captains and Harbors. Harbor-specific facts stay on the captain. A separate entity that captains reference. |
| 6 | When are revisions pooled? | A new runtime major version, model digest, served context or sampling starts an unpooled revision; patch releases pool automatically; the administrator can override. |
| 7 | Worker envelope limits and the no-captain case | A vessel default the Architect can tighten but not loosen. Escalate to a stronger compatible captain rather than wait. |
| 8 | Triage of suspected causes | A large `Unknown` share is fine at first. Classification rules are amendable data, with a triage view and re-application to past stages. |
| 9 | Cost of evaluation | A separate budget and concurrency limit (per inference host as well as overall). Quick is the default; Full only on request. |
| 10 | Profile vendor CLI captains too? | Yes. |
| 11 | Same-family evaluator? | Require a different family; allow the same family with a warning when nothing else is idle. |
| 12 | Battery language | The administrator chooses. Packs per language (C#, JavaScript, Python, Java, Rust, Go, PHP, Ruby to start), defined as data so new languages and non-code domains can be added. C# first. |
| 13 | Alert thresholds | Start with the defaults in [Alerts](#alerts), alerts only, no automatic action. Revisit after Phase 1 data. |
| 14 | Baseline rerun | Yes, all four models, 2 to 3 runs each, one at a time because they share a GB10 with 128 GB of shared memory. |

Still open: the exact field names, alert defaults after real data, and the per-vessel envelope defaults.

## Decision log

| Date | Decision | Notes |
|---|---|---|
| 2026-10-26 | Draft created | For discussion; nothing decided yet |
| 2026-10-26 | Coverage pass | Added the request map, evidence note, tool guidance level, rule tone setting, and the failure classification |
| 2026-10-26 | Revised after review | Root cause restated as one contributing cause. Corrected stale claims: stage checks run on every runtime by default, and Judge and Verifier output repair exists. Added the passed, failed, skipped and could-not-run distinction, the two truncation paths, safe repair rules, stable profile identity with immutable revisions, probes as observations, speed limited to inference timeouts, observed failure separate from suspected cause, stage-of-introduction attribution, cost per accepted result, the Worker eligibility envelope, and a reordered plan (baseline, battery, handoffs and repair, prompts, profiles and Evaluate, routing). Removed the competence scores, persona-fit floats, Layer 2 probes and nine rendering settings. |
| 2026-10-26 | Questions answered; plan adopted | The 14 open questions settled (see [Decisions](#decisions)): profile per model, not per Harbor; Quick probes moved to Phase 2; per-language battery packs defined as data; amendable failure classification; envelope as vessel default with escalation; separate evaluation budget. Phase 1 started under v1.1.0. |
| 2026-10-26 | Phase 2 step 1: Quick probes as a probe-only Evaluate | Six probes (served context, output contract, ASCII and format rules, tool calling, speed, determinism) run as ModelProbe Harbor jobs through the captain's own runtime; vendor CLIs are probed and inapplicable probes are recorded as could-not-run with the reason. Runs persist as ModelProbeRun with ModelProbeResult rows (probe set quick-1, Phase 1 provenance), are tagged as evaluations and write no stage outcome. Separate Evaluation settings: run and daily token budgets (overall and per inference host) and concurrency limits (overall, and one run per host by default). evaluate_captain (Quick only; Full is rejected until Phase 5), get_model_probe_run and list_model_probe_runs over REST, MCP and the clients; a minimal dashboard Evaluate button. Nothing changes routing or captain settings. |
| 2026-10-27 | Phase 2 step 2: fixture battery pack format and the C# pack | Packs are data (battery/<pack>/battery-pack.json, schema in battery/README.md): fixture, tasks, persona per task, grader (command, byteComparison, mutation, seededSet, rubric) and toolchain. The C# pack implements the ten template tasks. The runner resets the fixture (a forward commit) before each task, dispatches it on a fixture vessel created on request under the built-in ModelEvaluation pipeline (no landing), and grades it in its worktree; a missing toolchain is could-not-run, rubric tasks are pending evaluation with the raw output. Runs (BatteryRun, BatteryTaskResult) record seed, sampling settings, run count (default 3), battery version and configuration revision, share Evaluate's concurrency gate, and tag their stage outcomes IsEvaluation. list_battery_packs, run_battery and get_battery_run over REST, MCP and the .NET client. |
| 2026-10-30 | Phase 5 step 1: model profiles | ModelProfile (mdl_; mpr_ is taken by model probe runs) keyed by tenant, runtime, provider, model and served context, with editable capabilities, rendering overrides and notes; immutable ModelProfileRevision (mdv_) with runtime version, model digest, sampling, tool configuration and first seen. A new runtime major version, digest or sampling starts a revision; minor and patch releases pool; a served context change is a new profile. Compatibility marks are kept beside the revisions so revisions are never edited. Captain.ModelProfileId is set best effort from stage outcomes and Quick probe runs. REST /api/v1/model-profiles, MCP get_model_profile and list_model_profiles; migration 107. No routing change. |
| 2026-10-30 | Phase 5 step 2: scorecard | A read-only scorecard per (profile, persona), optionally per vessel, computed on request from stage outcomes, token usage and timing (no migration, no stored aggregate). Window: last 30 days or 50 missions, whichever is smaller, both per request. Rates are count of denominator with a 95% Wilson score interval. An attempt belongs to the newest revision it pools into that existed when it was recorded; revisions are separate groups unless marked compatible. Attribution: evaluation and battery missions, NotTheModel causes, and (unless a person classified the cause) Infra, OperatorAction, cancelled, interrupted and runtime-environment failures are excluded; an attempt in another persona is charged to that stage; a Judge verdict counts only when the reviewed work (the reviewed mission's last completed attempt before the review) is this profile's; Unknown and Mixed are their own buckets; each rule reports its count. Accepted result: the mission's final attempt is this profile's, completed, did not fail its checks and drew no Judge rejection; its cost sums every attempt on the mission and on earlier rework rounds. Finding categories, tool failure kinds and recovered stalls are returned as unavailable. REST GET /api/v1/model-profiles/{id}/scorecard, MCP get_model_scorecard. No alerts, suggested actions or routing change. |
| 2026-10-30 | Phase 5 step 3: alerts | Decision 13's defaults as built-in thresholds with per-persona overrides in settings, each with a minimum count; a rule whose scorecard metric is unavailable is inactive and reported, never invented. The cost rule compares tokens per accepted result with the median of the other profiles in the persona (2x, at least 5 accepted results on each side), because a median that includes the profile itself can never be exceeded twofold with two profiles. Escalations in a row is a new scorecard metric (the newest attempts escalated, stopping at the first that was not). One stored alert per (profile, revision group, persona, vessel or all, rule) (migration 108), resolved and reopened in place; a periodic pass (60 minutes) and on demand, read only. Suggested actions apply only when an operator asks, through the existing persona update and Evaluate, and are recorded on the alert; move down and remove act only on priority entries that name a model and select only this profile's captains. The compact template is unavailable until a prompt variant per profile and persona exists. Push is off by default. No automatic mode. |
