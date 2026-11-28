# Captain Routing and Persona Priorities

Armada assigns pending missions to available captains. Personas define the role and can also define an ordered hierarchy of captain classes. A role does not imply a provider, model, or deployment location: any persona can prefer Codex, Claude Code, Mux, or another runtime in any order.

## Define a hierarchy

Set `captainPriorities` on a persona through REST, MCP, or its configuration editor. Each entry matches all its supplied fields. Array order establishes priority; entries need at least one matching field.

| Field | Meaning |
|---|---|
| `runtime` | Exact Armada runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, OpenCode, Pi, ApiEndpoint, or Custom |
| `model` | Exact captain model identifier, ignoring case (maximum 1024 characters); no wildcard or substring matching |
| `endpoint` | For `runtime: Mux`, an exact endpoint name ignoring case. For `runtime: ApiEndpoint`, the exact, case-sensitive managed inference endpoint ID (`mep_...`), matched against `Captain.ModelEndpointId`. Maximum 1024 characters. Other runtimes cannot set this field. |
| `minimumTier` | Minimum effective Economy, Standard, or Premium tier; explicit captain tier overrides model classification (see Effective tier below) |

For example, a persona can prefer Codex, then Claude Code, then a Mux endpoint. Reverse the entries to prefer Mux. Model and endpoint fields select already-configured captains; routing does not modify their launch configuration. Configure the Mux captain's model and `RuntimeOptionsJson.endpoint` separately.

```json
{
  "captainPriorities": [
    { "runtime": "ApiEndpoint", "endpoint": "mep_your_grok_endpoint" },
    { "runtime": "Codex", "minimumTier": "Premium" },
    { "runtime": "ClaudeCode", "minimumTier": "Premium" },
    { "runtime": "Mux", "endpoint": "local", "model": "gpt-oss:120b" }
  ],
  "priorityAvailability": "FallThrough",
  "priorityExhaustion": "Wait"
}
```

## Select a specific API endpoint

Create an Inference model endpoint under Configuration > Endpoints, then configure an API Endpoint captain with that endpoint (`ModelEndpointId`). In persona create/edit, the priority target picker lists each visible inference endpoint by name, provider, and model alongside the CLI harnesses. Select the endpoint directly. Embedding endpoints are excluded. Dashboard, mobile (including the create/edit form's Captain priorities action), and TUI editors preserve its ID when saving, reordering, or duplicating priorities.

REST, MCP, and the TypeScript and .NET clients use the same contract:

```json
{ "runtime": "ApiEndpoint", "endpoint": "mep_your_grok_endpoint" }
```

This matches only captains bound to that endpoint, even if another endpoint uses the same provider and model. Renaming an endpoint does not change its identity. A deleted or unavailable endpoint remains an explicit criterion and follows the availability/exhaustion policy; it is never silently replaced by another API endpoint. Legacy `{ "runtime": "ApiEndpoint" }` entries still match any API captain. `Model` remains an optional constraint on the captain's explicit model override; it does not resolve the endpoint's default model. Routing selects configured captains and does not create a captain or change its launch configuration.

The .NET SDK uses the shared `PersonaCaptainPriority` model:

```csharp
persona.CaptainPriorities.Add(new PersonaCaptainPriority
{
    Runtime = AgentRuntimeEnum.ApiEndpoint,
    Endpoint = inferenceEndpoint.Id
});
await client.Personas.UpdateAsync(persona.Name, persona);
```

## API endpoint captains in pipelines

A persona priority selects a configured captain bound to a particular inference endpoint. The endpoint's `inferenceOptions` apply to every run of that captain, including pipeline stages and Harbor launches. The captain's explicit model override takes precedence over the endpoint's model; it does not override tool restrictions.

Pipeline missions prefer an eligible Harbor and run the API agent loop there, inside the mission's dock. Built-in file and process tools execute on that host against that worktree. Scoped MCP tools call their MCP server, normally the Admiral's mission-scoped Armada MCP service. Tool calls are executed by Armada, rather than by the inference provider. Interactive API captain sessions default to the Admiral unless the endpoint requires a Harbor.

Configure the endpoint in dashboard, mobile, or TUI create/edit, or use the REST/MCP/.NET SDK `inferenceOptions` object:

| Field | Default | Meaning |
| --- | --- | --- |
| `toolsEnabled` | `true` | Enable function tools. False sends plain chat without tool definitions or MCP discovery. |
| `readOnlyTools` | `false` | Only built-in tools classified read-only. Excludes shell, file changes, and all MCP tools because their effects are unclassified. |
| `mcpToolsEnabled` | `true` | Permit discovery and calls against the launch's scoped MCP server. Does not disable built-in tools. |
| `allowedTools` | `null` | Exact, case-sensitive names of otherwise permitted tools. `null` permits all eligible tools; `[]` permits none. Maximum 256 distinct names of at most 256 characters. |
| `maxIterations` | `100` | Maximum model turns per run, from 1 to 1000. Exhaustion exits with failure, so a pipeline cannot treat unfinished work as successful. |
| `maxTokens` | `null` | Optional output token limit per model turn, 1–10000000; provider/model limits still apply. |
| `temperature` | `null` | Optional temperature, 0–2; provider/model support still applies. Zero is preserved. |
| `topP` | `null` | Optional nucleus sampling setting, 0–1. |
| `requireHarbor` | `false` | Require an eligible Harbor owned by the launching user. Never fall back to execution on the Admiral. |
| `contextLength` | `null` | Optional context window, 512–2097152 tokens, sent as `num_ctx` on Ollama and Ollama Cloud. From 80% of the window, the oldest large tool outputs (all but the six most recent) are replaced with a one-line note saying what was removed, and assistant turns older than those six outputs keep only the path and a size note of the files they wrote (`write_file`, `edit_file`, `multi_edit`) and lose their reasoning, so long missions keep going; the system prompt and mission brief are never removed. The local estimate counts about 3.2 characters per token. A run stops with `context_limit_exceeded` only when its prompt reaches 95% and nothing is left to compact. Null uses the model default; Armada then reads the window the server runs (see [Context discovery](#context-discovery)) and compacts against it, and with no window found nothing is compacted. |
| `maxConcurrentMissions` | `null` | Optional cap, 1–64, on missions running at once against one server and model, across every captain and endpoint record that uses them. URLs are normalized (`localhost`, `127.0.0.1` and `::1`, a trailing slash, and `/v1` are the same server), two models on one server count separately, and a loopback URL gets its own budget on each Harbor and on the Admiral. Null is unlimited. |
| `streamingMode` | `Automatic` | `Automatic`, `On`, or `Off`. Streaming makes the endpoint's request timeout limit only the silence between chunks, so a long generation is not cut by an idle-based proxy or by the timeout itself, and each chunk is progress the stall detector sees. Tools run only after the provider signals a complete response; a stream that stops early is retried. `Automatic` streams on OpenAI-compatible, Ollama, and Ollama Cloud endpoints, in process and on a Harbor that advertises `api-inference-streaming-v1`; on an older Harbor it does not stream. `On` and `Off` always apply. |
| `streaming` | `false` | Compatibility form of `streamingMode` for clients before 1.5.0: reads true when the mode is `On`; writing true turns an `Automatic` mode `On`, and writing false changes nothing. A stored 1.4.0 `false` therefore reads as `Automatic`. |
| `firstResponseTimeoutMs` | `null` | Optional deadline, 1000–1800000 ms, for the model to start responding (cold model load, long prompt evaluation). With streaming it applies to each call's first chunk; without streaming it extends each call's timeout. Null uses the request timeout. |
| `toolOutputWindowPercent` | `null` | Optional share, 1–50 percent, of a known context window that one `read_file` or `run_process` result may fill (at about 3.2 characters per token). A longer read is paged with an offset to continue from, and longer process output is cut in the middle. The read maximum never drops below the default page of 200 lines and 24,000 characters, which does not change; process output keeps at least 8 KB per stream; both stop at 1 MB. Null uses 12 percent. With no known window (no `contextLength`) the fixed 1 MB read and 100 KB process caps apply, as does a Harbor older than 1.5.0. read_file refuses only files over 16 MB. |
| `repeatedToolCallLimit` | `null` | Optional number, 2 to 20, of identical consecutive tool calls (same tool, same arguments) after which each further repeat gets a corrective note in its result. Twice as many ends the run as `degenerate_output`, a task failure that escalation can route to another captain. Null uses 3. A Harbor older than 1.5.0 runs without the guard. |

Null generation values use provider defaults. These controls are for inference execution; embedding operations and health probes do not use the captain's generation limits. Existing endpoints retain their prior defaults after migration. Omitting `inferenceOptions` on update preserves the entire stored object. Supplying the object replaces it, with omitted members taking defaults. REST explicit null resets defaults; MCP callers reset by sending an object with the desired defaults. Blank allowlist text in the editors means all otherwise eligible tools; use Enable tool calls to turn all tools off.

Armada filters definitions before sending them and rejects policy-excluded tool names if the model nevertheless returns them. The active run snapshots the endpoint settings; edits apply to subsequent runs. Endpoint policy is an additional restriction on top of existing shell permissions: it cannot grant a command refused by the captain/vessel policy. A Harbor now explicitly applies that resolved shell policy. Approve in Armada for a remote API captain currently falls back to Refuse because that runtime does not advertise a remote permission-prompt bridge; Bypass is still an explicit captain/vessel permission choice. File tools are controlled by endpoint policy and do not trigger individual CLI approval prompts.

API captains expose large default MCP catalogs on demand: when more than 32 eligible remote tools are available and `allowedTools` is null, initial requests include built-ins plus `armada_discover_mcp_tools`. Search by exact tool name or task keywords to load up to four schemas per query; `query: "*"` and `offset` browse subsequent pages. Discovery only loads schemas and never executes a remote action. Loaded schemas remain available for that run. All otherwise permitted tools still route through the same caller-scoped MCP client, including direct calls to known names; this does not broaden permissions. Small catalogs, explicit allowlists and a real remote tool colliding with the discovery name retain eager exposure. Update the Harbor to apply this runtime optimization to Harbor-owned jobs. Accumulated conversation and tool-result size remain separate limits.

Harbors must advertise `api-inference-policy-v1` to run endpoints with nondefault policy or generation settings. Update the Harbor alongside the Admiral. Older Harbors are excluded for these endpoints rather than silently ignoring restrictions. In the same way, an endpoint with `contextLength` runs only on Harbors that advertise `api-inference-context-v1`, and one with `streamingMode` `On` or `firstResponseTimeoutMs` only on Harbors that advertise `api-inference-streaming-v1` (`Automatic` requires nothing and falls back to blocking calls).

A stalled call fails with `llm_timeout` and a reason naming the phase: the model never started responding (first-response deadline) or stopped sending mid-response (idle deadline). Both are transient, so the call is retried in the loop and the mission is resumed rather than failed. Streaming helps only when the deadline in front of the model is idle-based or time-to-first-byte, and the server sends bytes while it works; a proxy that limits total request time still needs a longer limit.

Measured behind a Conductor proxy whose endpoint `TimeoutMs` is 60 s (Conductor bounds the time until response headers, then streams the body without a limit): a blocking 5,200-token generation failed with 502 at 60 s, and the same generation streamed finished at 91 s. Ollama sends nothing while it loads a model, so a cold load longer than the proxy limit (an 81 GB model) fails even when streamed, and Ollama abandons the load when the proxy disconnects, so retrying cannot succeed. Armada reports such a long-wait 502 or 504 as a proxy deadline with what to change. Raise the proxy's limit (in Conductor, the virtual model runner's `TimeoutMs`; the smaller of it and the endpoint's applies) to cover cold loads, typically 300,000 ms to match Ollama's own `OLLAMA_LOAD_TIMEOUT` of 5 minutes, or keep large models loaded. A model that cannot load within Ollama's limit fails with Ollama's `timed out waiting for llama-server to start` whatever the proxy allows. Without `requireHarbor`, ordinary local fallback remains available when the dock can run locally; a dock belonging to a disconnected Harbor stays pending.

Example REST create body (also usable with `create_model_endpoint`):

```json
{
  "name": "read-only-grok-reviewer",
  "kind": "Inference",
  "provider": "XAI",
  "baseUrl": "https://api.x.ai",
  "model": "your-enabled-model",
  "apiKey": "your-provider-key",
  "inferenceOptions": {
    "toolsEnabled": true,
    "readOnlyTools": true,
    "mcpToolsEnabled": false,
    "allowedTools": ["read_file", "grep", "glob", "list_directory"],
    "maxIterations": 25,
    "maxTokens": 4096,
    "temperature": 0,
    "requireHarbor": true
  }
}
```

The .NET SDK uses the same contract: set `endpoint.InferenceOptions = new InferenceOptions { ReadOnlyTools = true, RequireHarbor = true, MaxIterations = 25 };` before `ModelEndpoints.CreateAsync` or `ModelEndpoints.UpdateAsync`. Dashboard/mobile share the typed `InferenceOptions` model.

### Remaining configuration queue

The following controls remain future work and are not implied by enabling function tools:

- Remote API captain approval prompts over the scoped MCP transport, with cancel/timeout and reconnect handling. Current behavior safely refuses shell calls when approval cannot be requested.
- Per-tool approval and argument/path/command policies, tool execution deadlines, maximum tool-result size, and an MCP read-only classification contract. The present allowlist controls names, not argument-level permissions or a filesystem sandbox.
- Per-endpoint retry/backoff and rate/concurrency/cost budgets. Request timeout exists separately as `timeoutMs`; turn limits are now configurable.
- Provider-specific reasoning effort, structured output/JSON schemas, native hosted tools, parallel calls and tool-choice settings. These require capability negotiation and PolyPrompt support per provider/model rather than assuming all endpoints support them.

### Context discovery

When an endpoint sets no `contextLength`, Armada reads the window the model server actually runs and uses it the way a
configured one is used: compaction from 80% and `context_limit_exceeded` at 95%. The sources are Ollama's `/api/ps`
(the served window of a loaded model), then the Modelfile's `num_ctx` from `/api/show`, and `max_model_len` from an
OpenAI-compatible `/v1/models` (vLLM). The model's trained maximum is never used, because Ollama does not serve it unless
asked. Other providers report nothing and run as before. The window is read when the endpoint is validated and before
an API captain's first turn (reused for 10 minutes; a server that reports nothing is not asked again for 10 minutes), and
stored on the endpoint as `discoveredContextLength` with its source. A configured `contextLength` always wins.

Discovery runs on the host that serves the run. An endpoint with `requireHarbor` is not discovered by validation; the
Harbor reads the window on its own host before the first turn and reports it on exit, and the Admiral stores it only from a
Harbor that advertises `api-context-discovery-v1`. An older Harbor runs without a window, as in 1.4.0. The window is part
of the model profile's identity, so a captain whose window becomes known moves to a profile for that window; the profile
records whether the window was configured, discovered, or measured.

## Busy classes and exhausted hierarchies

`priorityAvailability` defaults to `FallThrough`: Armada selects an eligible captain with a free slot from the first entry with an available match, trying lower entries when earlier ones are busy, missing, quarantined, or otherwise ineligible. `WaitForFirstPriority` considers only the first entry and waits for it, including when no configured captain matches. It never falls through and ignores the exhaustion setting.

With `FallThrough`, `priorityExhaustion: Wait` keeps unmatched work Pending until a later scheduling tick. `UseEligibleCaptain` explicitly permits normal persona/tier routing after every entry is exhausted. Empty priorities preserve legacy behavior regardless of these policies.

**Effective tier.** A captain's explicit `tier` is used as declared. Without one, the tier is its model profile's administrator-set tier (see [Tier from data](#tier-from-data)), and without that it comes from the model name: 8B parameters or fewer (`:7b`, `-3b`) and families such as Haiku, mini, nano, flash and gpt-oss are Economy; Opus, GPT-5 and later (including `gpt-6.1`), `o3` and `o4` as model names, and Gemini models with `-pro` are Premium; everything else, including an unknown model, is Standard. A Claude Code, Codex or Gemini captain with no model runs its vendor's default flagship model, so it meets any tier floor (`minimumTier`, a mission tier) but still ranks as Standard when dispatch prefers the cheapest captain that qualifies. No configuration is needed; set `tier` only to override the inference.

All hierarchy candidates must satisfy mission ownership/tenant boundaries, AllowedPersonas, the mission's tier floor, and availability. Within a matched class, PreferredPersona breaks ties, followed by the fewest active missions and then stable captain ID order. Hierarchy choices are reevaluated on every assignment attempt and are not saved as RequestedCaptainId.

## Captain slots

Each captain has `MaxConcurrency` slots, one per running mission: 4 by default for ApiEndpoint and Mux captains and 1 for every other runtime (see [CAPTAINS.md](CAPTAINS.md#captain-capacity)). Routing considers only captains with a free slot that are active, `Idle` or `Working`, and not in an exclusive Planning, Refining or Analyzing session; a `Stopping`, `Stalled` or `Quarantined` captain takes no new missions while its running missions continue. Inside each routing bucket (requested captain, persona default, hierarchy entry, tier), captains whose PreferredPersona matches come first, then the captain with the fewest active missions, then (outside a hierarchy) the least recently updated, then captain ID. Equivalent work therefore spreads across captains instead of filling one captain's slots first. When every eligible captain is full, the mission waits with `CaptainConcurrencyLimit`; see [SCHEDULING.md](SCHEDULING.md#capacity).

## Missions without a persona

A mission dispatched without a persona (a plain dispatch with no pipeline, `create_mission`, a fleet action without a persona) is routed as a **Worker**: the Worker persona's captain priorities, AllowedPersonas fences, and the voyage's Worker or wildcard captain assignment apply to it. Its stored Persona stays empty.

## When a captain cannot start

If launching the chosen captain fails (for example its CLI is not installed on the host the mission was placed on), the mission returns to Pending and assignment backs off: 30 s, 1 m, 2 m, 4 m, then every 5 minutes. The mission's assignment blocker shows `LaunchFailing` with the reason and the next attempt time. After six failures caused by a missing CLI the mission fails (Infra) with a reason saying to connect the Harbor and turn on Require Harbor for launch. A captain marked Stalled by a provider condition (usage limit, authentication, model unavailable) is returned to Idle after 15 minutes and tried again.

The backoff is recorded against the captain as well as the mission: the retry goes to a different eligible captain when there is one. A launch that fails because of the captain's own configuration (for example an API captain with no usable endpoint) quarantines the captain, so it is not picked first for every waiting mission. A failed captain moves straight to Stalled or Quarantined; it is never briefly Idle in between.

When every connected Harbor that can run the captain's runtime is at capacity, the mission waits for a slot. It does not start on the Admiral instead, where the runtime may not be installed.

## When a model endpoint hiccups

A captain that exits because its model endpoint failed transiently (HTTP 500, 502, 503, or 504, a dropped connection, a timeout, a plain HTTP 429 rate limit, a model server whose runner crashed or is still loading) or because a mux run reached its iteration or token budget is relaunched in the same dock, with its edits so far intact and a note in its instructions to inspect `git status` and `git diff` and continue, up to `maxRecoveryAttempts` times. The voyage keeps running; only when the attempts are used up does the mission fail. Iteration and budget stops do not count toward the captain's crash-loop quarantine. An exit with no provider error Armada recognizes (`Unclassified`) is resumed once, then handed to the voyage's planner.

A 429 quarantines the captain only when the provider says the quota is gone: an `insufficient_quota` or usage-limit error, a stated reset time, or a `Retry-After` longer than 10 minutes. Otherwise it is backpressure: API captains wait out a `Retry-After` of up to 30 seconds inside the run, and a longer one ends the run and the resume waits for it. HTTP 402 is always a usage limit.

Each resume waits first, starting at `transientResumeDelaySeconds` (5) and doubling with jitter up to 60 seconds, so a struggling model server is not hit with an immediate retry. API captains also retry a failed inference call inside the run before giving up. When three runs in a row against one base URL fail at the endpoint level (transient errors or model unavailable), across all captains that use it, Armada stops giving missions to those captains for two minutes; the next clean run closes the breaker. The breaker, a mission's launch backoff, and the start of a transient outage are kept in the database, so restarting the Admiral does not reset them. `AgentSettings.MaxConcurrent` and the endpoint's `maxConcurrentMissions` limit how many missions run at once.

## Checks and escalation between stages

A captain of any runtime that finishes a work stage (any persona except Product Manager, Architect, Judge, and Recorder) is checked before the stage moves on: the vessel's workflow-profile `BuildCommand` and unit-test command run in the dock (on its Harbor), within `stageCheckTimeoutMinutes` (default 15). The unit-test command is `UnitTestCommand` (full) unless `[ARMADA:UNIT-TESTS] abbreviated` appears in the mission description or, failing that, in the stage persona's rendered prompt, in which case `AbbreviatedUnitTestCommand` runs (falling back to the full command when unset). `[ARMADA:UNIT-TESTS] full` forces the full suite; the mission description wins over the persona prompt. The chosen scope is recorded as `unitTestScope` on the `StageCheckResult`. Before this release only Mux and API-endpoint stages were checked; set `verifyAllStageRuntimes` to false to restore that. A failure resumes the captain with the command output. A failed, timed-out, or unavailable check does not count as a pass. A check that fails on the stage's base commit as well is reported as a pre-existing failure, not blamed on the captain. Every check is recorded on its mission as a `mission.stage_check` event (list it with `enumerate` on events, filtered by mission) whose payload is the `StageCheckResult`: `status` is `Passed`, `Failed`, `Skipped`, or `CouldNotRun`, and `reason` says why (`RuntimeNotChecked`, `ReviewPersona`, `VerifierReportedBlocker`, `NoDock`, `NoWorkflowProfile`, `NoCommands`, `NoNewCommit`, `HarborDisconnected`, `TimedOut`, `Error`), with the commands, the failed command and exit code, and any pre-existing failures. Only `Passed` means the stage was verified. Files the check itself produces are reverted, so build output is never committed to the branch. When a stage still cannot finish after its bounded resumes, its work is committed on the branch and one recovery attempt goes to a different eligible captain: the voyage's Architect (else Product Manager), an idle alternative from Worker priorities, the voyage's Judge (including its configured captain before the first review runs), or another eligible worker. Tenant/user visibility, persona restrictions, quarantine, and minimum tier apply. This also works for Worker > Judge pipelines with no planning stage. Recovery waits for its selected captain instead of silently falling back to the failed one. If there is no alternative, or recovery fails, the mission fails.

### Escalation ladder

A persona's `EscalationLadder` (RS-5.6) lists captain classes, lowest rung first, with the same matcher fields as
`CaptainPriorities`. When a work stage's captain cannot finish (its resumes are spent, or the model profile's
`EscalateAfterResumes` is reached; an `Unclassified` exit past its resume; two unparsable tool calls in a row; two iteration-limit stops in a row; repeated
no-op completions; Judge rejections past the rework budget; optionally an open endpoint breaker with
`EscalateOnOpenEndpointBreaker`), the mission returns to Pending on the same branch, pinned to the first eligible
captain on a rung above the failed captain's rung (rung 1 when the failed captain matches none). Idle captains are
preferred within a rung; past the top rung there is no escalation. With an empty ladder, the hop must reach a captain
with a strictly higher effective tier (the voyage planner, Worker priorities, the reviewer, then any eligible Worker, in
that order). Every hop counts toward `MaxEscalations`; strict captain assignments, and Product Manager, Architect, Judge
and Recorder stages, never escalate. Standalone missions escalate too. Migration 136 adds the ordered
`persona_escalation_rungs` child table on all four providers; the dashboard, mobile and terminal persona editors edit it
with the same rung controls as the priorities.

The escalated captain also gets the previous attempt's memory (RS-5.7) in the mission's escalation note: the failure
kind (mission kind, runtime classification and exit code), the black box rule hint for that failure (the strongest
specific rule the catalog matches; the catch-all rule is left out), the files changed on the branch since the dock
started (up to 25 named), and the last 2000 characters of the previous output, redacted with the black box redactor.
Parts that are not known are left out. The previous output is then cleared from the mission so it is not read as the
escalated attempt's output.

Autonomous rescues (RS-5.8) reuse the same selector and memory: from the second rescue of an incident on, the rescue is
pinned to the escalation choice with the captain of the failed rescue excluded, and every rescue brief carries the last
failed attempt's failure kind, rule hint and output tail. See [Delivery operations](DELIVERY_OPERATIONS.md).

### How recovery is chosen

One engine, `RecoveryPolicyEngine` (RS-5.10), decides every recovery from where the failure happened, its kind, the
attempts already spent and the captain's recovery policy (its model profile over the global settings). It returns the
actions to try in order; the caller carries out the first one it can and falls through to the next when it cannot (for
example, no stronger captain is free). The decisions:

| Where | Order of actions |
|---|---|
| Agent exit, interrupted (negative exit code) | Redispatch while `MaxNoOpRedispatchAttempts` (at least 3) lasts, then fail |
| Agent exit, transient or task failure | Escalate first if the endpoint breaker is open (opt-in), two tool calls in a row were unparsable, or the captain stopped at its iteration limit twice in a row; resume in place within `MaxResumes` (a task failure stops resuming at `EscalateAfterResumes`; a transient outage also resumes within its grace); escalate; fail |
| Agent exit, Unclassified | Resume within `UnclassifiedResumes`, escalate, fail |
| Agent exit, usage limit, auth failure or crash | Fail (the captain may be quarantined) |
| Process lost (health monitor) | Redispatch an interruption within the budget, else fail |
| No-op or unfinished completion | Redispatch within `MaxNoOpRedispatchAttempts`, escalate, fail |
| Broken output contract | One format-only turn that restates the stage's own output (accepted only when it adds nothing), then rerun within `OutputRepairAttempts` (never on review-gated stages), else fail |
| Judge rejection | Rework within `JudgeReworkRounds`; past it, or on unchanged work, escalate one hop per round up to `MaxEscalations`; then stop |
| Recovery incident | Wait for a rescue in flight; rescue within `MaxMissionRecoveryAttempts`; then notify `RecoveryExhausted` and leave it for a human |

The Admiral logs each runtime-exit decision at debug level, for example `recovery for mission msn_x after TaskFailure:
Resume, then Escalate, then Fail`.

## Admission order and typed blockers

Every Pending mission goes through the same gates in the same order on each dispatch pass, and the first gate that
says no becomes the mission's one `MissionAssignmentBlocker`. The dispatch log, the assignment blocker in the
dashboard, TUI, mobile app and API, and the stuck-admission monitor all read that one result, so they cannot tell
different stories about why a mission is waiting.

1. **The vessel.** A missing vessel (`VesselMissing`) or one whose local repository and working directory are the same
   folder (`VesselMisconfigured`) stops the mission outright.
2. **Launch backoff.** A mission whose last launch failed waits out its backoff (`LaunchFailing`, with the reason and
   the next attempt time).
3. **Readiness.** The stage it depends on must be finished and its handoff prepared (`DependencyNotFinished`,
   `DependencyHandoffPending`), its parent mission must be finished (`ParentNotFinished`), and an integration stage
   waits for the voyage's Workers (`WaitingForVoyageWorkers`).
4. **Vessel safety.** Broad-scope missions and branch locks on the vessel (`VesselBroadScopeMissionActive`,
   `BroadScopeWaitingForVessel`, `BranchInUse`), then the vessel and voyage limits (`VesselVoyageLimit`,
   `VoyageMissionLimit`).
5. **Global capacity.** `GlobalMissionLimit` when the server's mission limit is full.
6. **Usage selection**, when it is on, with its own reason codes under `NoEligibleCaptain`.
7. **The captain gate.** Routing as this page describes (priorities, tier, the context fit filter, the Judge strength
   rule, ranking), then captain, runtime and endpoint capacity and dock placement: `NoCaptains`, `NoEligibleCaptain`
   (with a `selectionReasonCode` such as `context_too_small` or `judge_strength`), `NoIdleCaptain`,
   `CaptainConcurrencyLimit`, `RuntimeMissionLimit`, `EndpointMissionLimit`, `EndpointUnavailable`,
   `PlacementWaiting`.

A mission of a voyage that is still `Building` is never admitted. A mission that nothing blocks shows
`AwaitingDispatch` until the next pass, or a launch policy such as a required Harbor connection, lets it start. Each blocker carries a summary that names what clears it, and for captain reasons a per-captain
detail line. [SCHEDULING.md](SCHEDULING.md#why-a-mission-is-waiting) explains each reason from the operator's side.

## Capability-aware routing (1.5.0)

Four rules use what Armada measures about each model (archive/RELIABILITY_SIMPLICITY_PLAN.md RS-8.4 to RS-8.7).

### Served context is a hard filter

A captain whose served context cannot hold the mission's prompt is not eligible. The served context is the
endpoint's configured context, else the context its server reported (context discovery), else the model profile's
served context; an unknown context never excludes a captain. The prompt is estimated from the parts the
instructions file never trims: the mission description, the persona's output contract and a 2,048-token allowance for
the role, rules and progress signals, plus the handoff's share of the context for a stage that reads an earlier
stage. It must fit in 75 percent of the context; the rest is kept for tool output and the reply. The rule applies to
requested and pinned captains too. When every available captain fails it, the blocker is `NoEligibleCaptain` with
`selectionReasonCode` `context_too_small`, and each captain's status says how many tokens the prompt needs.

### Tier from data

A captain's effective tier is its explicit `tier`, else its model profile's tier (`tiering.tier`, set by an
administrator with `PUT /api/v1/model-profiles/{id}`), else the tier classified from the model name. With
`RoutingData.SuggestTiers` on (default off), the model alert pass also scores each profile from its recent stage
outcomes and, when the data disagrees with the tier routing uses, records a suggestion on the profile
(`tiering.suggestedTier`, `suggestedReason`, `suggestedSamples`, `suggestedUtc`) and raises a `TierSuggestion` model
alert in Needs You. Nothing changes until an administrator clicks Apply tier (`POST /api/v1/model-alerts/{id}/apply`
with `action` `ApplyTier`) or dismisses it (`Ignore`, 7 days by default; a suggestion of another tier shows again at
once). Only personas with at least `RoutingData.MinimumSamples` attempts count; a pooled score of 85 percent or more
suggests Premium, 65 percent or more Standard, otherwise Economy.

### Ranking within a tier

Ranking is off by default because it slows dispatch: every pass reads outcome statistics for the
candidates. In the 100-captain admission benchmark (`benchmarks/baselines/1.5.0/admission-100.json` against
`admission-100-rank-off.json`), admission latency was p50 1.90 s and p95 3.07 s with ranking on, against
1.47 s and 1.76 s with it off: about 29% slower at the median and 74% slower at p95.

With `RoutingData.RankByOutcomes` on (default off), captains of one routing bucket are ordered by their model's
recorded results for the mission's persona: completion without failed checks, Judge PASS verdicts on the work, no-op
rate, tool-call validity and repair rate, each attempt weighted by age with a `RoutingData.HalfLifeDays` half-life
(default 14). A model needs `RoutingData.MinimumSamples` attempts for the persona (default 20); captains without
enough data follow the ranked ones in the 1.4.0 order (load, then last update). Persona captain priorities, the Judge
strength rule and the Judge's preference for captains that did not author the reviewed work still come first. An
assignment from a ranked bucket records a `mission.routing_ranked` event listing each captain's score and samples.

### Judge strength rule

A pipeline's `judgeStrength` sets the rule for the voyages it creates (copied to the voyage at creation):

| Value | Effect |
|-------|--------|
| `Off` (or null) | Any eligible captain judges, as in 1.4.0. Pipelines and voyages stored before 1.5.0 read as Off. |
| `Wait` | A Judge whose effective tier is below the highest tier among the captains that ran the stages it reviews is not eligible. The mission waits with `selectionReasonCode` `judge_strength` until a strong enough captain is free. The default for pipelines created through REST, MCP or WebSocket. |
| `ProceedAndFlag` | A Judge that meets the rule is preferred; when none is available the best available Judge runs and the mission gets a `mission.judge_strength_exception` event (gate G8). |

With the rule on, a Judge of another model family than every author is preferred over one of the same family. A
requested captain (dispatch payload or voyage override) is not held to the rule. `ExcludeReviewAuthorsFromJudge` now
compares model identity: every captain on an author's model (the model name without a provider prefix, whatever
runtime serves it) is excluded, not only the author's captain.

## Explicit overrides and legacy defaults

An explicit mission RequestedCaptainId takes precedence, followed by the voyage's exact per-persona captain assignment, then its wildcard (`*`) assignment. Those choices retain legacy behavior: an available requested captain bypasses AllowedPersonas and tier checks; a busy or deleted one falls back through ordinary persona/tier routing. Ownership and quarantine checks still apply. These explicit overrides bypass the hierarchy.

A nonempty hierarchy supersedes Persona.DefaultCaptainId. Without a hierarchy, the default captain remains a preference: it is tried first and can fall back by tier. Dispatch pickers leave the captain on automatic routing when priorities are configured, even if a legacy default remains stored. An operator can still select an explicit override.

Pipelines and Architect-generated Worker missions resolve their personas through the same assignment path. Repeated stages using the same persona use the same hierarchy. Configure different personas when their routing should differ.

## Configuration and diagnostics

The dashboard's persona create/edit forms include ordered matching fields, add/remove/move controls, and availability policies. Mobile persona detail opens the same structured controls from Captain priorities. The terminal forms support add, edit, delete, and Alt+Up/Down ordering. Duplication copies the hierarchy and its policies.

Persona create accepts all routing fields. Partial REST/MCP updates preserve omitted routing fields; `captainPriorities: []` clears the hierarchy. Invalid classes or enum values are rejected. Legacy defaultCaptainId changes only when an update carries it, on every surface (REST, MCP, WebSocket): an omitted value keeps it, and an empty string clears it. REST and WebSocket also accept an explicit null; MCP tool arguments are schema-checked as strings, so MCP uses the empty string.

A Pending mission's assignmentBlocker uses the hierarchy to decide whether any captain is eligible. Successful hierarchy assignment logs the persona, chosen captain ID, and matched priority number. No matches stay Pending rather than failing the pipeline. See [SCHEDULING.md](SCHEDULING.md) for other blockers.

Startup migration 83 adds priority_availability and priority_exhaustion columns to personas and an ordered persona_captain_priorities child table across SQLite, PostgreSQL, MySQL, and SQL Server. Parent and child writes are atomic; deleting a persona cascades to its priorities. Existing personas receive an empty hierarchy and retain their routing behavior.

API captains reject empty terminal replies and require the standalone completion marker when the bootstrap prompt requires it. They request continuation up to twice within the endpoint's existing iteration budget; repeated incomplete replies become a recoverable task failure, not a successful stage or a captain crash. The final-result file is replaced even when the terminal text is empty. Ordinary Ask Armada replies do not require a mission marker. Each inference turn logs its iteration, finish reason, token usage when supplied, text length, and tool-call count without logging credentials or private reasoning. Update the Harbor as well as the Admiral to receive the runtime fixes.

A rejected rework that leaves the same commit as a preceding Worker escalates early rather than spending another round on the same captain (read-only missions are exempt). Judge-driven rework is bounded to `JudgeReworkRounds` ordinary rounds (default 2) and then one escalated round per hop, up to `MaxEscalations` hops (default 2). By default (`ExcludeReviewAuthorsFromJudge` false) the following Judge prefers an idle captain that did not run an earlier stage of the chain it reviews, falls back to an author captain when no other eligible captain is idle, and honors a requested or persona default captain even when it is an author. With `ExcludeReviewAuthorsFromJudge` true, every captain that ran an earlier stage of the chain is excluded from the Judge's assignment, including explicit captain overrides; configure an independent eligible reviewer or that review will wait, and the assignment blocker says that the authors were excluded.

Judges are instructed to restore dependencies in fresh worktrees and distinguish an unavailable check from a source defect. Built-in `run_process` uses Bash with `pipefail` on Unix when `/bin/bash` exists, so an upstream failure cannot be hidden by piping to `tail`; otherwise it uses `/bin/sh`. Use the tool's `timeout_ms` parameter rather than relying on an external GNU `timeout` executable on macOS. Direct executable argument vectors and Windows commands retain their existing behavior.
