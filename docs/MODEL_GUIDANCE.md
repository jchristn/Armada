# Model Guidance

How to choose and evaluate models to run as Armada captains, persona by persona.

Models differ in five ways that matter to Armada: how well they reason, how well they code, whether they can call
tools reliably, how fast they are, and which tasks they happen to be good at. No model is best at all five, and the
eight built-in personas need very different mixes of them. A model that is a poor Worker can be a fine Recorder; a
model that writes excellent code can still be a poor Judge.

So start from the persona, not the model. Decide what the role needs, check the hard requirements, run a small
evaluation inside Armada, and then encode what you learned in the persona's captain priorities
([CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md)).

This guide does not rank specific models. Rankings go stale in weeks. It tells you what to look for, where to look,
and how to measure it yourself; [Observations](#observations) records dated results from real Armada runs.

## Contents

- [The traits that matter](#the-traits-that-matter)
- [Hard requirements](#hard-requirements)
- [Persona by persona](#persona-by-persona)
- [Summary matrix](#summary-matrix)
- [Evaluating a candidate model](#evaluating-a-candidate-model)
- [Slow models and Armada's timers](#slow-models-and-armadas-timers)
- [Turning results into routing](#turning-results-into-routing)
- [Public sources](#public-sources)
- [Observations](#observations)
- [Maintaining this guide](#maintaining-this-guide)

## The traits that matter

| Trait | What it means in Armada | How it shows up when it is missing |
|---|---|---|
| Reasoning | Holding a goal, the codebase, and constraints together; decomposing work; judging whether a change is right | Missions sliced wrong, plans that ignore dependencies, a Judge that passes broken work or blocks good work |
| Coding | Making correct, coordinated edits to existing files in the repository's own style | Builds that break, edits in the wrong place, a method written but never registered, house style ignored |
| Tool use | Emitting well-formed tool calls, reading tool results correctly, and acting on them in a loop | Malformed tool-call JSON (an Ollama 500 through Mux), repetition loops, iteration limits hit, line numbers from `read_file` pasted into code |
| Speed | Time to first token, output tokens per second, and how many tokens the model writes to finish | Stages that outlast Armada's timers, slow pipelines, a captain marked stalled while it is still thinking |
| Task fit | Strengths a model has beyond general ability: prose, UI sense, test writing, terse review, following a strict output contract | A capable model that still writes vague product briefs, padded reviews, or ignores the required markers |

Two more properties are not traits of intelligence but decide whether a model can work at all:

- **Context window.** The mission prompt, the vessel's project context and style guide, and the files the captain
  reads must fit. Local models are often served with a smaller window than the model supports (for example Ollama's
  `num_ctx`); check what the endpoint actually serves, not the model card.
- **Contract fidelity.** Armada reads structured output: the Architect's `armada-plan` block, the Judge's
  `[ARMADA:VERDICT]` line and `## Affected Case` section, a Worker's commit and completion line. A model that
  paraphrases instead of following the format fails these stages no matter how good its reasoning is. Armada repairs
  only what it can decide without guessing, and records each repair as a `mission.contract_repaired` event: a missing
  or malformed `[ARMADA:RESULT] COMPLETE` line from a Worker or Test Engineer whose dock branch holds new work, work
  left uncommitted (committed by Armada), exactly one Judge verdict written in a nearby form such as
  `**[ARMADA:VERDICT] PASS**`, and exactly one valid plan outside a closed `armada-plan` fence. Two verdicts, an
  ambiguous verdict, two plan blocks, or a stage with no completion line and nothing on its branch are not repaired.

## Hard requirements

Check these before judging quality. A model that fails one should not run a persona that needs it.

| Requirement | Needed by | How to check |
|---|---|---|
| Reliable tool calling through the captain's runtime | Every persona that edits files or runs commands (Worker, Usability Engineer, Test Engineer, Linter), and any persona on the ApiEndpoint, Mux, OpenCode, or pi runtimes | The captain's Available Tools view in the dashboard shows what the runtime exposes. Then run one small mission and look for malformed tool calls and repetition loops in the mission log |
| Context window that fits the vessel | Every persona; most of all the Architect and Judge, which read the most | Compare the endpoint's served context size with the vessel's project context length plus a typical diff |
| Follows Armada's output contracts | Architect, Judge (strict); Worker (commit and completion line) | Run the persona once and check that Armada parsed the result: missions created, a verdict recorded, work produced |
| Stops cleanly | Every persona | No iteration-limit hits and no runaway output on a small task |

Armada's runtimes differ here. Claude Code, Codex, Gemini, and Cursor are vendor harnesses with their own tool loop
tuned to their own models. ApiEndpoint, Mux, OpenCode, and pi drive a model you choose through a generic tool loop, so
tool-calling reliability is the model's responsibility. Most tool-use failures seen so far were local models on Mux.

## Persona by persona

Each section lists what the persona does, what matters most (in order), what failure looks like in Armada, and which
public signals are worth reading. Persona definitions are in [PERSONAS_GUIDE.md](PERSONAS_GUIDE.md).

### Product Manager

Frames the dispatched work as a product: user value, success criteria, workflows, validation expectations, staying
inside the dispatched scope. First stage of `FullPipeline`.

- **Matters most:** reasoning about users and scope, then clear writing. Coding ability barely matters. Tool use is
  light (reading the repository and its docs). Speed matters little: it runs once per voyage.
- **Failure looks like:** generic briefs that could apply to any project, scope creep the Architect then plans for,
  success criteria nobody can test.
- **Good choice:** a strong general reasoning model. This is a cheap place to spend on quality, because one good brief
  shapes every later stage.
- **Public signals:** human-preference rankings (LMArena) and general-intelligence indexes (Artificial Analysis,
  Epoch AI). Coding benchmarks say little about this role.

### Architect

Reads the codebase and decomposes the goal into right-sized missions, emitted as an `armada-plan` block
(`contractVersion` 4; `[ARMADA:MISSION]` blocks are still accepted as a fallback). In v4 each mission names a
`parent` only when it needs another mission's code, and lists its `ownedPaths`; two missions that can run in
parallel must not own the same file, or Armada rejects the plan and asks for a repair.

- **Matters most:** reasoning and decomposition, then contract fidelity, then a large usable context. It must read
  enough code to understand dependencies, so tool use matters, but mostly for reading.
- **Failure looks like:** missions too big for the Worker model to finish (the Worker hits iteration limits or
  stalls), missions too small to be meaningful, slices that depend on each other in the wrong order, deep parent chains
  that serialize work that could run in parallel, parallel slices that own the same files, malformed plans that Armada
  cannot parse.
- **Good choice:** the strongest reasoning model you have. Size the missions to the Worker you actually use: a weaker
  Worker needs smaller, more explicit missions. Long-task reliability research (METR) is a useful guide to how much a
  model can finish in one go.
- **Public signals:** reasoning-heavy benchmarks on Epoch AI's hub, Artificial Analysis's intelligence index, METR
  time horizons. Your own data matters more: how often its missions land without rework.

### Worker

Makes the code change, builds, tests, and commits. The default persona; any mission without a persona runs as a
Worker. Usually the most frequent and most expensive stage.

- **Matters most:** tool-calling reliability and coding accuracy equally, then staying on task over a long
  edit, build, and test loop, then speed. It must commit its work and print its completion line; a Worker that ends
  with no commit and no completion line is re-dispatched as unfinished work. When the line is missing or malformed but
  the branch holds new work, Armada inspects the repository and accepts the work instead of failing the stage.
- **Failure looks like:** malformed tool calls, broken builds handed downstream, coordinated edits done halfway (the
  method written but never registered in the suite list), encoding and line-ending damage, stray backup files picked
  up by the leftover-work commit, never committing.
- **Good choice:** this is where a capable local model can pay off most, because it does the most token-heavy work.
  The hybrid trial ([Observations](#observations)) found that model family and tuning mattered more than size: two
  27 to 30 billion parameter coding-tuned models beat a 120 billion parameter general model on correctness, tool calls,
  and speed. Work stages are built and tested before they hand off when the vessel's workflow profile defines build
  or test commands, so a weaker Worker is usually caught early, but each catch costs a rework round. A vendor-runtime
  stage that commits nothing is not checked.
- **Public signals:** agentic coding benchmarks (SWE-bench Verified, Terminal-Bench) and tool-calling benchmarks
  (BFCL). Edit-format benchmarks (Aider Polyglot) show whether a model edits existing files precisely. None of them
  test C# or your repository's style, so treat them as a shortlist, not an answer.

### Usability Engineer

Refines the implemented work from the user's side: edge cases, error messages, consistency with the surrounding
product. Edits code and documentation after the Worker.

- **Matters most:** coding and tool use (it edits files), then product sense and writing.
- **Failure looks like:** cosmetic churn with no user benefit, edits outside the mission's scope, or a broken build
  from careless edits.
- **Good choice:** the same class of model as the Worker usually works. In the hybrid trial the most careful local
  model made real improvements here (clarifying documentation for an edge case). A model with good UI or prose
  preferences helps on user-facing work.
- **Public signals:** human-preference coding rankings (LMArena's Code Arena, formerly WebDev Arena) for UI-heavy
  vessels; otherwise the Worker's signals.

### Test Engineer

Analyzes the Worker's diff, finds missing coverage, and writes tests in the repository's existing patterns.

- **Matters most:** coding accuracy on existing test files, tool use, and following house conventions. Adding a test
  usually means two coordinated edits (write the test and register it), which the hybrid trial found to be the
  hardest step for weaker models.
- **Failure looks like:** tests that are never registered and so never run, tests that pass trivially, duplicated
  suites, a broken test project.
- **Good choice:** a Worker-class model. It runs after the Worker on a working build, so it is a reasonable place to
  try a cheaper model once the Worker is reliable.
- **Public signals:** the same as the Worker.

### Linter

Checks the mission's changed code and documentation for style and correctness, fixes clear in-scope violations, and
reports judgment calls. Stays strictly inside the files the mission changed.

- **Matters most:** precision and staying in scope, then speed (it runs on every pipeline that includes it), then tool
  use. Deep reasoning matters less.
- **Failure looks like:** reformatting whole files, "fixing" things the style guide does not require, wandering into
  files the mission did not touch, or, with a weak model, spending its whole run trying to repair a broken build.
- **Good choice:** a fast, inexpensive model that follows instructions literally. A good fit for local models.
- **Public signals:** instruction-following and tool-use benchmarks (BFCL, tau-bench) more than coding benchmarks.

### Judge

Reviews the Worker's diff through three lenses (Correctness, Blast Radius, Source Fidelity) and emits exactly one
`[ARMADA:VERDICT] PASS|FAIL|NEEDS_REVISION` line. A blocking verdict must name a concrete affected case; a shallow
PASS is rejected.

- **Matters most:** reasoning and skepticism, then contract fidelity, then the ability to read a diff in context.
  Coding knowledge matters for reading, not writing. Speed matters little.
- **Failure looks like:** rubber-stamp PASS verdicts, vague NEEDS_REVISION verdicts with no affected case, missing or
  duplicated verdict lines, verdicts based on prose instead of a build or test run.
- **Good choice:** a strong reasoning model from a different model family than the Worker. Armada prefers a captain
  that did not author the work (set `ExcludeReviewAuthorsFromJudge` to forbid self-review); a different family also avoids shared blind spots. Check that the Judge's
  runtime can actually build and test on the Harbor: in the hybrid trial the Codex Judge's sandbox could not run
  `dotnet`, so its verdicts came from reading source and earlier stages' reports.
- **Public signals:** reasoning benchmarks (Epoch AI's hub, Artificial Analysis). Your own data is far better: how
  often its PASS verdicts later hold up, and whether its findings name the file, line, and fix.

### Recorder

Reviews the voyage and distills durable memories into the vessel's context, the Armada memory store, and external
memory facilities. Last stage of `FullPipeline` and `Recorded`.

- **Matters most:** summarization and judgment about what is worth keeping, then tool use (it writes memories).
  Coding ability barely matters. It runs once per voyage, so speed matters little.
- **Failure looks like:** memories that restate the mission instead of capturing lessons, duplicated memories,
  memories that contradict the code, or bloated vessel context that every later mission pays to read.
- **Good choice:** a mid-tier general model is usually enough. Quality still matters, because bad memories are read
  by every later mission.
- **Public signals:** general-intelligence indexes and human-preference rankings.

## Summary matrix

How much each trait matters to each persona.

| Persona | Reasoning | Coding | Tool use | Speed | Contract fidelity | Context window |
|---|---|---|---|---|---|---|
| Product Manager | High | Low | Light | Low | Medium | Medium |
| Architect | Critical | Medium (reads code) | Medium (reads) | Low | Critical | High |
| Worker | High | Critical | Critical | Medium | High (commit, completion) | High |
| Usability Engineer | Medium | High | High | Medium | Medium | Medium |
| Test Engineer | Medium | Critical | Critical | Medium | Medium | Medium |
| Linter | Low | Medium | High | High | Medium (stay in scope) | Medium |
| Judge | Critical | High (reads) | Medium (build, test) | Low | Critical | High |
| Recorder | Medium | Low | Medium | Low | Medium | Medium |

Where to spend: Architect and Judge (quality multiplies across every mission they touch). Where to save: Linter, then
Test Engineer and Recorder. Where a good local model pays off most: Worker, because it does the most token-heavy work.

## Evaluating a candidate model

Public benchmarks give you a shortlist. Only a run inside Armada, on your repository, tells you whether a model works
as a captain. Make the evaluation repeatable so results compare.

### Set up

1. Pick a small, real task on a throwaway vessel (or a scratch branch of a real one) that needs a coordinated change:
   new code in an existing file, tests in the existing suite, and a documentation update. Write the prompt once and
   reuse it verbatim for every model.
2. Record the starting commit and reset the vessel to it before every run.
3. Create one captain for the candidate model and route only the persona under test to it with captain priorities.
   Keep every other persona on a known model so differences come from the candidate.
4. Write down the conditions: Armada server build, Harbor, runtime and its version (for example the Mux version),
   endpoint settings (temperature, context size, request timeout, iteration limit), and the pipeline.

### What to record

Most of these come straight from Armada: mission events (`captain.launched` to `mission.work_produced`), the token
usage records, the mission log, and Judge verdicts.

| Measure | Why it matters |
|---|---|
| Outcome: landed, and whose code landed | A run that lands because a frontier model rewrote the code is a failure for the candidate |
| First pass builds and passes tests | The best single predictor of a good Worker |
| First pass time and launches | Speed, and how often it needed resuming |
| Malformed tool calls, repetition loops, iteration-limit hits | Tool-use reliability |
| Resumes in place | Every resume is a failure Armada recovered from |
| Committed its own work | A Worker that never commits leans on the leftover-work commit |
| Judge verdicts and what was rejected | Separates behavior problems from encoding, scope, and style problems |
| Rework rounds, and whether work escalated to another captain | The real cost of a weaker model |
| Tokens in and out, active minutes, wall time | Cost and throughput |
| Encoding and line endings kept, no stray files | Repository hygiene; these cause rework churn for every model |

### Read the results

- Separate model problems from tooling problems. In the hybrid trial several rejections came from a runtime bug (an
  editor that dropped the UTF-8 byte order mark), a repository that tracked a generated file, and a sandbox that
  blocked the Judge's builds. These hit every model equally.
- Look at the kind of rejection, not just the count. Five rejections for line endings are cheaper to fix than one for
  wrong behavior.
- Run more than once before concluding. One run shows whether a model can do the job; three show whether it does it
  reliably.

## Slow models and Armada's timers

A slow model is not a bad model. The most reliable local model in the hybrid trial was also the slowest. But Armada
has timers that assume a captain is making progress, and a slow model can trip them. Raise these on the server (the
dashboard's Server settings) before running slow models.

| Setting | Default | What it guards | When to raise it |
|---|---|---|---|
| `StageCheckTimeoutMinutes` | 15 | Each build or test command run after a stage | Above your slowest real build plus test run; a full test run that takes 30 minutes needs about 45 |
| `StallThresholdMinutes` | 10 | Time since a captain's last heartbeat before it is marked stalled | When a model can go quiet for long stretches (long thinking, long prompt evaluation) |
| `MaxConcurrentMissions` | 0 (unlimited) | Missions running at once | Lower it when one local inference server serves several captains; parallel requests slow every one of them |
| `ReviewTimeoutMinutes` | 1440 | How long a mission waits for human review | Rarely; it is not model-dependent |

Outside Armada, check the timeouts in front of the model:

- **Proxies and gateways.** A proxy that times out after 60 seconds cuts off cold model loads and long prompt
  evaluations. Raise it or keep the model warm (for Ollama, `keep_alive`).
- **The runtime's own limits.** Request timeouts and agent iteration limits on Mux or an API endpoint. A careful model
  may need more iterations; a wandering model hitting the limit is a quality signal, not a reason to raise it.

## Turning results into routing

Encode what you learned in each persona's captain priorities ([CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md)):

- **Put the proven model first** and a known fallback second. With `priorityAvailability: FallThrough`, a busy first
  choice falls through to the next entry; with `WaitForFirstPriority`, missions wait for it.
- **Keep the Judge on a different family than the Worker.** List the Judge's priorities so it never lands on the
  Worker's model.
- **Do not trust tier inference for quality.** A captain's effective tier is inferred from the model name (size and
  family keywords) unless you set it. It is a cost heuristic, not a measurement. After evaluating a model, set the
  captain's `tier` explicitly so `minimumTier` routes on what you measured.
- **Pin by model or endpoint when it matters.** A priority entry can match an exact model or a specific managed
  inference endpoint, so a persona can prefer one evaluated model rather than any captain on a runtime.
- **Revisit after upgrades.** A new model version, a new runtime version, or a change to the vessel's build can change
  results. Re-run the evaluation and add a dated row to [Observations](#observations).

## Public sources

These help build a shortlist and explain behavior. Read them with care:

- **Harness and scaffold matter.** Agentic scores depend on the agent loop, tools, and prompts used. A model's score in
  someone else's harness is not its score in Armada's.
- **Most benchmarks are not your stack.** Several are Python-only or competitive-programming problems. None measures
  C#, your repository's conventions, or Armada's output contracts.
- **Contamination.** Public test sets leak into training data. Prefer benchmarks that refresh problems or report
  results by date.
- **Self-reported and aggregated numbers disagree.** Vendor-reported scores and third-party aggregators often
  conflict. Use the benchmark's own leaderboard.
- **Saturation.** When the top models are within a point of each other, the benchmark no longer separates them.

| Source | What it measures | Most useful for | Notes |
|---|---|---|---|
| [SWE-bench Verified](https://www.swebench.com/) | Resolving real GitHub issues in Python repositories, checked by the repositories' tests | Worker, Test Engineer | The standard agentic coding benchmark; Python only; heavily scaffold-dependent; near saturation at the top |
| [Terminal-Bench](https://www.tbench.ai/leaderboard) | Completing tasks in a real terminal environment | Worker, Test Engineer, Linter | Closest to a captain working in a dock; scores are per agent and model pair |
| [Aider leaderboards](https://aider.chat/docs/leaderboards/) | Editing existing code across several languages in a fixed edit loop (Polyglot) | Worker, Usability Engineer | Good signal for precise edits to existing files; languages do not include C# |
| [LiveCodeBench](https://livecodebench.github.io/) | Code generation, self-repair, and execution on contest problems released after a date | Worker (rough signal) | Contamination-aware by release date; algorithmic problems, not repository work |
| [Berkeley Function Calling Leaderboard](https://gorilla.cs.berkeley.edu/leaderboard.html) | Tool and function calling, including multi-turn and agentic cases | Every tool-using persona; the hard requirement | The best public signal for whether a model can drive ApiEndpoint, Mux, OpenCode, or pi |
| [tau-bench](https://taubench.com/) ([repository](https://github.com/sierra-research/tau2-bench)) | Multi-turn tool use while following a policy, with a simulated user | Linter, Worker, contract fidelity | Tests following rules over a long interaction, close to following Armada's contracts |
| [METR time horizons](https://arxiv.org/abs/2503.14499) | The length of task (in human expert time) a model completes at a given success rate | Architect (mission sizing), Worker | Explains why big missions fail on weaker models; updated by METR over time |
| [Artificial Analysis](https://artificialanalysis.ai/leaderboards/models) | A composite intelligence index, output speed, latency, and price, per model and per API provider | Every persona; the speed trait | Independent speed and latency measurements; shows that the same model differs across providers |
| [LMArena](https://lmarena.ai/leaderboard) | Blind human preference, including a coding arena (Code Arena, formerly WebDev Arena) | Product Manager, Usability Engineer, Recorder | Preference, not correctness; good for writing and UI sense |
| [Epoch AI Benchmarking Hub](https://epoch.ai/benchmarks) | Many hard benchmarks run with consistent, documented settings | Architect, Judge | Reasoning and science benchmarks; consistent methodology across models |
| [OpenRouter rankings](https://openrouter.ai/rankings) | Real usage by token volume, with category views such as programming and tool calls | Shortlisting | Popularity, not quality; useful for seeing what other coding agents use |
| [Ollama model library](https://ollama.com/search) | Local models, sizes, context lengths, and a tools capability badge | Local captains on Mux or ApiEndpoint | Check for the tools badge before trying a local model as a Worker |
| Model cards and provider docs | Context window, tool-calling format, recommended sampling settings | Every persona | The served context and sampling settings often differ from the defaults; set them on the endpoint |

## Maintaining this guide

- Keep the persona sections about what each role needs. Put model-specific findings in Observations, not in the
  persona sections, so the guidance stays true as models change.
- When a public source changes its address or stops updating, fix or remove its row.
- When a persona's contract changes (a new marker, a new verdict rule), update its section and the
  [Summary matrix](#summary-matrix).
