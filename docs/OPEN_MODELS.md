# Open Models in Mixed Pipelines

How to run Qwen3.8 and other open models in an Armada pipeline next to Opus and GPT-6.1, and what Armada does to keep
those pipelines finishing.

The point of a mixed pipeline is cost. A frontier model is the right Architect and the right last line of review, but
most of the hours in a voyage go to Worker and Test Engineer stages, and an open model on hardware you already own can
do a lot of that work. Until 1.5.0 the trouble was that an open model failed in ways a frontier harness never does: a
tool call written as text, a verdict in the wrong format, a 32K window that could not hold the prompt, a cold load that
tripped the stall timer. Each of those turned into a failed voyage and a human picking it up. Armada 1.5.0 absorbs the
mechanical failures, routes around the ones it cannot absorb, and escalates to a stronger model with the evidence when
an open model really cannot do the work.

## Contents

- [The shape of a mixed pipeline](#the-shape-of-a-mixed-pipeline)
- [Set up the endpoint](#set-up-the-endpoint)
- [Served context and the prompt](#served-context-and-the-prompt)
- [Streaming and timeouts](#streaming-and-timeouts)
- [Concurrency on one server](#concurrency-on-one-server)
- [Escalation ladder](#escalation-ladder)
- [Judge strength](#judge-strength)
- [Tiers, suggestions and ranking](#tiers-suggestions-and-ranking)
- [The reference pipelines](#the-reference-pipelines)
- [Measured results](#measured-results)
- [Known limits](#known-limits)

## The shape of a mixed pipeline

The reference pipelines in `benchmarks/config/reference-pipelines.json` are the configurations the 1.5.0 gate runs, and
they are a good place to start. The one most teams will want first is Gate-A: Opus plans as the Architect, Qwen3.8 does
the Worker and Test Engineer stages, and GPT-6.1 judges. The planning and the final review are the stages where a
weaker model does the most damage, and they are short; the implementation stages are long and forgiving, because the
build, the tests and the Judge all check them.

Gate-D is the stress case: every persona runs on Qwen3.8, and the escalation ladder moves a stage to GPT-6.1 and then
Opus only when Qwen cannot finish it. It tells you how far the open model gets on its own, which is the number that
decides how much frontier spend a mixed pipeline saves. The same pipeline runs with inkling-2-mini, DeepSeek 4.1, GLM
and Kimi in place of Qwen to check that the results are not specific to one model family.

## Set up the endpoint

An open model reaches Armada as an Inference model endpoint (Configuration > Endpoints in the dashboard, or
`POST /api/v1/model-endpoints`) plus an API Endpoint captain bound to it (`runtime` `ApiEndpoint`, `modelEndpointId`
the endpoint's id). The captain runs Armada's own tool loop against the endpoint, inside the mission's dock, so the
model needs nothing installed beyond the server that hosts it.

| Server | `provider` | Typical `baseUrl` | Notes |
|---|---|---|---|
| Ollama | `Ollama` | `http://127.0.0.1:11434` | Uses Ollama's native `/api/chat`. `contextLength` is sent as `num_ctx`, and Armada sends a `keep_alive` warm-up before the first turn so a cold load does not eat into it. |
| Ollama Cloud | `OllamaCloud` | the provider's URL | Same format, with an API key. Useful as a fallback for models you do not host yourself. |
| vLLM | `OpenAICompatible` | `http://127.0.0.1:8000` | Reports its served window as `max_model_len`, which Armada reads. Start it with tool calling enabled for the model's parser. |
| LM Studio | `OpenAICompatible` | `http://127.0.0.1:1234` | Set `contextLength` by hand to the window you loaded the model with. |
| A proxy in front of any of these | `OpenAICompatible` or `Ollama`, matching what the proxy speaks | the proxy's URL | Check the proxy's own request timeout; see [Streaming and timeouts](#streaming-and-timeouts). |

A minimal Ollama endpoint looks like this. Use the model tag your server lists; `qwen3.8:27b` is only an example.

```json
{
  "name": "qwen-local",
  "kind": "Inference",
  "provider": "Ollama",
  "baseUrl": "http://127.0.0.1:11434",
  "model": "qwen3.8:27b",
  "inferenceOptions": {
    "toolsEnabled": true,
    "maxIterations": 100,
    "maxConcurrentMissions": 2
  }
}
```

Validate it once (`POST /api/v1/model-endpoints/{id}/validate`, or Validate in the dashboard) and run the model probes
from its model profile. Validation reads the served context window. The tool-calling probe records on the profile
whether the model's tool calls and MCP tools actually work through this server, and the Speed probe records its tokens
per second, which the timeouts use. Neither probe says anything about the quality of the work; the
[Model guidance](MODEL_GUIDANCE.md) evaluation steps cover that.

The rest of the endpoint options (tool allowlists, read-only tools, Harbor placement, sampling) are documented in
[Captain routing](CAPTAIN_ROUTING.md#api-endpoint-captains-in-pipelines).

## Served context and the prompt

The single most common reason an open model failed a stage in 1.4.0 was a window too small for the prompt it was
given. A Qwen that supports 128K is very often served at 32K or less, because that is what fits in memory, and the
model card does not tell you that. Armada now works from the served window, never the trained maximum.

When the endpoint sets no `contextLength`, Armada reads the window from the server: Ollama's `/api/ps` for a loaded
model, then the Modelfile's `num_ctx`, or `max_model_len` from an OpenAI-compatible `/v1/models`. The result is stored
on the endpoint as `discoveredContextLength` with its source, and a configured value always wins.

Three things follow from the served window. First, the prompt variant: with nothing set on the voyage, captain or
model profile, a window at or below `PromptVariantCompactMaxContextTokens` (32768) gets the Compact prompt and one at or
below `PromptVariantLeanMaxContextTokens` (65536) gets Lean, with the description, acceptance criteria, handoff and output
contract kept whole in both. Second, the whole instructions file plus the tool schemas is held to a quarter of the
window, and the API runtime compacts old tool output from 80 percent of it. Third, routing: a captain whose window
cannot hold the mission's prompt in 75 percent of its size is not picked at all, and the mission waits with
`context_too_small` instead of failing halfway through.

API captains also see only the Armada tools their stage needs. A Judge gets five tools, about 835 schema tokens, where
the full catalog is about 62,000 tokens; the rest stays reachable through `armada_discover_mcp_tools`. On a 32K window
that difference is most of the budget.

## Streaming and timeouts

Open models are slow, and slowness looked like failure to Armada's timers. Two changes fix most of it.

Streaming is on by default (`streamingMode` `Automatic`) for Ollama, Ollama Cloud and OpenAI-compatible endpoints.
With streaming, the endpoint's request timeout limits only the silence between chunks, so a long generation is not
cut off, and each chunk counts as progress for the stall detector. A proxy that limits total request time still needs
a longer limit; streaming does not help there. Ollama sends nothing while it loads a model, so a very large model that
loads slower than the proxy's limit fails even when streamed. Raise the proxy limit to cover cold loads (Ollama's own
load timeout is 5 minutes) or keep the model loaded.

Timeouts scale with measured speed. When the Speed probe measures a model slower than
`TimeoutScaleReferenceTokensPerSecond` (default 30), the stall threshold, tool-wait limit and maximum runtime are the
global values multiplied by the reference over the model's rate, at most four times. A Qwen at 10 tokens per second
gets a 30 minute stall threshold where the global value is 10. A model profile can also set each timeout by hand, plus
a first-output time that applies only until a mission's first output, so a model reading a long prompt is not marked
stalled. The profile's `effectiveTimeouts` shows each value in effect and where it came from.

## Concurrency on one server

One GPU serving four parallel requests serves each of them slower, and past a point all four time out. Set
`maxConcurrentMissions` on the endpoint to what the server can actually carry. The cap is counted per server and model,
not per endpoint record: `http://127.0.0.1:11434`, `http://localhost:11434/` and `http://localhost:11434/v1` are one
server, so two records that spell the URL differently share one budget. Two different models on one server are counted
separately, and a circuit breaker opened by one does not block the other. A loopback URL means a different machine on
each Harbor, so each Harbor and the Admiral get their own budget for it.

## Escalation ladder

A persona's escalation ladder is what makes Gate-D possible. It lists captain classes from the lowest rung up, for
example Qwen3.8 on an API endpoint, then GPT-6.1 on Codex, then Opus on Claude Code. When a work stage's captain cannot
finish, the mission moves on the same branch to the next rung with an eligible captain, and the new captain is told
how the last attempt failed: the failure kind, the black box rule hint, the files it changed, and the end of its
output.

The triggers are designed to catch an open model that is stuck rather than one that is slow: resumes spent, an exit
Armada cannot explain past its one resume, two unparsable tool calls in a row, two iteration-limit stops in a row,
repeated no-op completions, and Judge rejections past the rework budget. A model profile's `EscalateAfterResumes` can
escalate sooner for a model you know gives up early. `MaxEscalations` (default 2) caps the hops, and a persona without a
ladder escalates only to a strictly higher tier, never sideways.

Escalation is the expensive path, so most of 1.5.0 is about not needing it. The ApiEndpoint runtime runs tool calls an
open model writes as text, corrects a malformed call or a repeat loop with a targeted turn instead of resending the same
request, answers a call to a tool that does not exist with the list of tools that do, and strips reasoning that leaks
into the reply. A Judge or Architect whose report breaks the output contract gets one tool-less call to restate its own
output in the right format before the stage is rerun, and captains can submit their result as typed JSON
(`armada_submit_plan`, `armada_submit_verdict`, `armada_submit_result`) that is checked in the same call.

## Judge strength

A review is only worth what the reviewer can see. If Qwen wrote the code and another Qwen reviews it, the review shares
the author's blind spots. A pipeline's `judgeStrength` rule holds the Judge to the strength of the work it reviews:
with `Wait`, a Judge whose tier is below the highest tier among the reviewed stages' captains is not eligible and the
review waits (`judge_strength`); with `ProceedAndFlag`, the best available Judge runs and the mission records a
`mission.judge_strength_exception`. New pipelines default to `Wait`. With the rule on, a Judge from a different model
family than the authors is preferred, which is why Gate-A judges Qwen's work with GPT-6.1.

Gate-C turns this around on purpose: Opus does the work and Qwen is staffed as the Judge. Under the rule the Qwen review
either waits for a strong enough captain or runs and is flagged, so a weaker Judge of stronger work is never silently
trusted.

## Tiers, suggestions and ranking

Tiers drive most routing decisions, and a tier inferred from a model name is a guess. Set the tier on the model profile
once you have run the model on real work; every captain on that model then routes on it. With
`RoutingData.SuggestTiers` on (off by default), Armada scores each profile from its recorded outcomes and raises a
TierSuggestion alert in Needs You when the data disagrees with the tier in use. Nothing changes until someone clicks
Apply tier.

Within a tier, outcome ranking is on by default (`RoutingData.RankByOutcomes`). Captains whose model has at least 20
results for the persona (`MinimumSamples`, weighted with a 14-day half-life) are tried in order of their record before
captains without enough history. A new model is not penalized for being new: below the sample size it keeps the 1.4.0
order. The Reliability tab under Captains in the dashboard shows completion, failures, repairs, escalations and the
`Unclassified` rate per model and persona, which is where you see whether a model has earned its place.

## The reference pipelines

| Pipeline | Architect | Worker | Test Engineer | Judge | What it shows |
|---|---|---|---|---|---|
| Gate-A | Opus | Qwen3.8 | Qwen3.8 | GPT-6.1 | The everyday mixed pipeline |
| Gate-B | GPT-6.1 | Qwen3.8 (fanned out) | | Opus | An Architect fanning out to open-model Workers |
| Gate-C | Qwen3.8 | Opus | | Qwen3.8 | A weaker Judge of stronger work, under the Judge strength rule |
| Gate-D | Qwen3.8 | Qwen3.8 | Qwen3.8 | Qwen3.8 | Open model alone, escalating Qwen3.8 to GPT-6.1 to Opus |
| Gate-D variants | | | | | Gate-D with inkling-2-mini, DeepSeek 4.1, GLM and Kimi |

The gate runner is `src/Armada.Gate`; [benchmarks/README.md](../benchmarks/README.md) explains how to start an isolated
Admiral and run it. Where each open model is served lives in `benchmarks/config/gate-endpoints.local.json`, which is
git-ignored; copy `gate-endpoints.example.json` to start one. A finished run writes a report and uploads it to the
Admiral, and the latest report shows on the Reliability tab and at `GET /api/v1/reliability/gate-reports/latest`.

## Measured results

The 1.5.0 numbers are recorded here after the release gate run, measure by measure, with the 1.4.0 baseline beside
them. The measures are the twelve gate measures in `RELIABILITY_SIMPLICITY_PLAN.md`: voyages finishing with no human
intervention (target 99 percent), no stuck missions, no duplicate work, format failures repaired without a rerun, the
`Unclassified` share of runtime failures (under 2 percent), no Judge PASS on a seeded defect, no open-model Judge of its
own tier's work without an exception, crash consistency, dispatch latency, queries per dispatch pass, and escalations
that went strictly up.

<!-- RS-10.6 gate numbers -->

## Known limits

Armada can make an open model's mistakes cheaper; it cannot make the model smarter. A model that picks the wrong file,
misreads a tool result or stops before the work is done fails the stage, and escalation is the remedy, not repair. The
corrections the runtime adds are bounded on purpose (two correcting turns in a row, one format restatement), so a model
that needs one on every turn finishes slowly or not at all.

Some limits are in the plumbing:

- Context discovery covers Ollama and OpenAI-compatible servers that report `max_model_len`. Anything else needs
  `contextLength` set by hand, and with no known window nothing is compacted.
- The text tool-call recovery, the correcting turns and the repeat guard are in the ApiEndpoint runtime. Mux,
  OpenCode and pi run their own tool loops; they get reasoning stripping and error classification, not the rest.
- A Harbor older than 1.5.0 keeps its 1.4.0 behavior for streaming, context discovery and the tool-output limits, and
  is marked "Harbor update recommended". Update Harbors with the Admiral.
- Outcome ranking and tier suggestions need history. A fresh install routes as 1.4.0 did until each model has 20
  results for a persona.
- Escalation spends frontier tokens. A pipeline where the open model escalates on most stages costs more than running
  the frontier model from the start, because the failed attempt is paid for too. Watch the escalation count on the
  Reliability tab before committing a persona to an open model.
