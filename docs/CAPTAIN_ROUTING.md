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
await client.UpdatePersonaAsync(persona.Name, persona);
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
| `contextLength` | `null` | Optional context window, 512–2097152 tokens, sent as `num_ctx` on Ollama and Ollama Cloud. From 80% of the window, the oldest large tool outputs (all but the six most recent) are replaced with a one-line note saying what was removed, so long missions keep going; the system prompt, mission brief and assistant turns are never removed. A run stops with `context_limit_exceeded` only when its prompt reaches 95% and nothing is left to compact. Null uses the model default, and nothing is compacted. |
| `maxConcurrentMissions` | `null` | Optional cap, 1–64, on missions running at once against this endpoint's base URL across every captain that uses it. Null is unlimited. |
| `streaming` | `false` | Stream responses. The endpoint's request timeout then limits only the silence between chunks, so a long generation is not cut by an idle-based proxy or by the timeout itself. Tools run only after the provider signals a complete response; a stream that stops early is retried. |
| `firstResponseTimeoutMs` | `null` | Optional deadline, 1000–1800000 ms, for the model to start responding (cold model load, long prompt evaluation). With streaming it applies to each call's first chunk; without streaming it extends each call's timeout. Null uses the request timeout. |

Null generation values use provider defaults. These controls are for inference execution; embedding operations and health probes do not use the captain's generation limits. Existing endpoints retain their prior defaults after migration. Omitting `inferenceOptions` on update preserves the entire stored object. Supplying the object replaces it, with omitted members taking defaults. REST explicit null resets defaults; MCP callers reset by sending an object with the desired defaults. Blank allowlist text in the editors means all otherwise eligible tools; use Enable tool calls to turn all tools off.

Armada filters definitions before sending them and rejects policy-excluded tool names if the model nevertheless returns them. The active run snapshots the endpoint settings; edits apply to subsequent runs. Endpoint policy is an additional restriction on top of existing shell permissions: it cannot grant a command refused by the captain/vessel policy. A Harbor now explicitly applies that resolved shell policy. Approve in Armada for a remote API captain currently falls back to Refuse because that runtime does not advertise a remote permission-prompt bridge; Bypass is still an explicit captain/vessel permission choice. File tools are controlled by endpoint policy and do not trigger individual CLI approval prompts.

API captains expose large default MCP catalogs on demand: when more than 32 eligible remote tools are available and `allowedTools` is null, initial requests include built-ins plus `armada_discover_mcp_tools`. Search by exact tool name or task keywords to load up to four schemas per query; `query: "*"` and `offset` browse subsequent pages. Discovery only loads schemas and never executes a remote action. Loaded schemas remain available for that run. All otherwise permitted tools still route through the same caller-scoped MCP client, including direct calls to known names; this does not broaden permissions. Small catalogs, explicit allowlists and a real remote tool colliding with the discovery name retain eager exposure. Update the Harbor to apply this runtime optimization to Harbor-owned jobs. Accumulated conversation and tool-result size remain separate limits.

Harbors must advertise `api-inference-policy-v1` to run endpoints with nondefault policy or generation settings. Update the Harbor alongside the Admiral. Older Harbors are excluded for these endpoints rather than silently ignoring restrictions. In the same way, an endpoint with `contextLength` runs only on Harbors that advertise `api-inference-context-v1`, and one with `streaming` or `firstResponseTimeoutMs` only on Harbors that advertise `api-inference-streaming-v1`.

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

The .NET SDK uses the same contract: set `endpoint.InferenceOptions = new InferenceOptions { ReadOnlyTools = true, RequireHarbor = true, MaxIterations = 25 };` before `CreateModelEndpointAsync` or `UpdateModelEndpointAsync`. Dashboard/mobile share the typed `InferenceOptions` model.

### Remaining configuration queue

The following controls remain future work and are not implied by enabling function tools:

- Remote API captain approval prompts over the scoped MCP transport, with cancel/timeout and reconnect handling. Current behavior safely refuses shell calls when approval cannot be requested.
- Per-tool approval and argument/path/command policies, tool execution deadlines, maximum tool-result size, and an MCP read-only classification contract. The present allowlist controls names, not argument-level permissions or a filesystem sandbox.
- Per-endpoint retry/backoff and rate/concurrency/cost budgets. Request timeout exists separately as `timeoutMs`; turn limits are now configurable.
- Provider-specific reasoning effort, structured output/JSON schemas, native hosted tools, parallel calls and tool-choice settings. These require capability negotiation and PolyPrompt support per provider/model rather than assuming all endpoints support them.

## Busy classes and exhausted hierarchies

`priorityAvailability` defaults to `FallThrough`: Armada selects an eligible captain with a free slot from the first entry with an available match, trying lower entries when earlier ones are busy, missing, quarantined, or otherwise ineligible. `WaitForFirstPriority` considers only the first entry and waits for it, including when no configured captain matches. It never falls through and ignores the exhaustion setting.

With `FallThrough`, `priorityExhaustion: Wait` keeps unmatched work Pending until a later scheduling tick. `UseEligibleCaptain` explicitly permits normal persona/tier routing after every entry is exhausted. Empty priorities preserve legacy behavior regardless of these policies.

**Effective tier.** A captain's explicit `tier` is used as declared. Without one, the tier comes from the model name: 8B parameters or fewer (`:7b`, `-3b`) and families such as Haiku, mini, flash and gpt-oss are Economy; Opus, GPT-5, o3/o4 and Gemini Pro are Premium; everything else, including an unknown model, is Standard. A Claude Code, Codex or Gemini captain with no model runs its vendor's default flagship model, so it meets any tier floor (`minimumTier`, a mission tier) but still ranks as Standard when dispatch prefers the cheapest captain that qualifies. No configuration is needed; set `tier` only to override the inference.

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

A captain that exits because its model endpoint failed transiently (HTTP 500, 502, 503, or 504, a dropped connection, a timeout) or because a mux run reached its iteration or token budget is relaunched in the same dock, with its edits so far intact and a note in its instructions to inspect `git status` and `git diff` and continue, up to `maxRecoveryAttempts` times. The voyage keeps running; only when the attempts are used up does the mission fail. Iteration and budget stops do not count toward the captain's crash-loop quarantine.

Each resume waits first, starting at `transientResumeDelaySeconds` (5) and doubling with jitter up to 60 seconds, so a struggling model server is not hit with an immediate retry. API captains also retry a failed inference call inside the run before giving up. When three runs in a row against one base URL fail at the endpoint level (transient errors or model unavailable), across all captains that use it, Armada stops giving missions to those captains for two minutes; the next clean run closes the breaker. `AgentSettings.MaxConcurrent` and the endpoint's `maxConcurrentMissions` limit how many missions run at once.

## Checks and escalation between stages

A captain of any runtime that finishes a work stage (any persona except Product Manager, Architect, Judge, and Recorder) is checked before the stage moves on: the vessel's workflow-profile `BuildCommand` and unit-test command run in the dock (on its Harbor), within `stageCheckTimeoutMinutes` (default 15). The unit-test command is `UnitTestCommand` (full) unless `[ARMADA:UNIT-TESTS] abbreviated` appears in the mission description or, failing that, in the stage persona's rendered prompt, in which case `AbbreviatedUnitTestCommand` runs (falling back to the full command when unset). `[ARMADA:UNIT-TESTS] full` forces the full suite; the mission description wins over the persona prompt. The chosen scope is recorded as `unitTestScope` on the `StageCheckResult`. Before this release only Mux and API-endpoint stages were checked; set `verifyAllStageRuntimes` to false to restore that. A failure resumes the captain with the command output. A failed, timed-out, or unavailable check does not count as a pass. A check that fails on the stage's base commit as well is reported as a pre-existing failure, not blamed on the captain. Every check is recorded on its mission as a `mission.stage_check` event (list it with `enumerate` on events, filtered by mission) whose payload is the `StageCheckResult`: `status` is `Passed`, `Failed`, `Skipped`, or `CouldNotRun`, and `reason` says why (`RuntimeNotChecked`, `ReviewPersona`, `VerifierReportedBlocker`, `NoDock`, `NoWorkflowProfile`, `NoCommands`, `NoNewCommit`, `HarborDisconnected`, `TimedOut`, `Error`), with the commands, the failed command and exit code, and any pre-existing failures. Only `Passed` means the stage was verified. Files the check itself produces are reverted, so build output is never committed to the branch. When a stage still cannot finish after its bounded resumes, its work is committed on the branch and one recovery attempt goes to a different eligible captain: the voyage's Architect (else Product Manager), an idle alternative from Worker priorities, the voyage's Judge (including its configured captain before the first review runs), or another eligible worker. Tenant/user visibility, persona restrictions, quarantine, and minimum tier apply. This also works for Worker > Judge pipelines with no planning stage. Recovery waits for its selected captain instead of silently falling back to the failed one. If there is no alternative, or recovery fails, the mission fails.

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

A rejected rework that leaves the same commit as a preceding Worker escalates early rather than spending another round on the same captain (read-only missions are exempt). Judge-driven rework remains bounded to two ordinary rounds and one escalated attempt. By default (`ExcludeReviewAuthorsFromJudge` false) the following Judge prefers an idle captain that did not run an earlier stage of the chain it reviews, falls back to an author captain when no other eligible captain is idle, and honors a requested or persona default captain even when it is an author. With `ExcludeReviewAuthorsFromJudge` true, every captain that ran an earlier stage of the chain is excluded from the Judge's assignment, including explicit captain overrides; configure an independent eligible reviewer or that review will wait, and the assignment blocker says that the authors were excluded.

Judges are instructed to restore dependencies in fresh worktrees and distinguish an unavailable check from a source defect. Built-in `run_process` uses Bash with `pipefail` on Unix when `/bin/bash` exists, so an upstream failure cannot be hidden by piping to `tail`; otherwise it uses `/bin/sh`. Use the tool's `timeout_ms` parameter rather than relying on an external GNU `timeout` executable on macOS. Direct executable argument vectors and Windows commands retain their existing behavior.
