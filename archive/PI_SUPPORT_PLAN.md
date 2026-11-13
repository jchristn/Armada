# Plan: pi as an Armada agent runtime

Status: Phases 1-4 are implemented (mission msn_mvsftf4t_Lz5EG4dfqbP, 2026-10-28) and Phase 5 (the ApproveInArmada
permission gate extension) plus the Refuse, Windows prompt-delivery and crash-recovery gaps are implemented (mission
msn_mvsj2k9t_7TXuBAhn0rz, 2026-10-28); every task below is marked [DONE]. Deviations from the plan are listed in
"Implementation notes and deviations" below. Still open (see "Remaining work"): manual verification on a real Windows
host, RPC mode, and session resume. When this plan was written, paths marked
(NEW) did not exist; every other Armada path existed in the repository at commit dbef448c9. Paths that belong to the pi
repository (`docs/json.md`, `src/modes/print-mode.ts`, `examples/extensions/...`) are relative to
`packages/coding-agent/` in github.com/earendil-works/pi, and names such as `mcp.json`,
`models.json`, `auth.json`, `settings.json`, `SYSTEM.md` and `.pi/mcp.json` are pi configuration
files on the user's machine or in a working directory, not Armada repository files.

The OpenCode runtime is the reference implementation throughout, because it is the most recent
CLI runtime added to Armada and it touches almost every layer a new CLI runtime needs.

## Sources

Facts about pi were read from the pi repository (github.com/earendil-works/pi, `main` at commit
a92283056, 2026-10-27; package `packages/coding-agent`, npm `@earendil-works/pi-coding-agent`
v1.4.0, binary `pi`). The old name `badlogic/pi-mono` redirects to the same repository. The
pages below are under
`https://github.com/earendil-works/pi/blob/main/packages/coding-agent/`:

- `docs/quickstart.md` - install methods, Node requirement
- `docs/cli.md` and `src/cli/args.ts` (help text, around lines 300-345) - CLI flags
- `docs/json.md` and `src/modes/json-event.ts` - `--mode json` event stream
- `docs/message-types.md` - assistant message, usage, stopReason
- `docs/rpc.md`, `docs/rpc-commands.md`, `docs/rpc-extension-ui.md` - RPC mode
- `docs/sdk.md` - SDK
- `docs/configuration.md`, `docs/environment-variables.md`, `docs/providers.md`, `docs/models.md`
- `docs/mcp.md` - built-in MCP extension
- `docs/security.md`, `docs/extensions.md`, `examples/extensions/permission-gate.ts`
- `docs/session-format.md`, `docs/sessions.md`
- `docs/windows.md`
- `src/modes/print-mode.ts` (around lines 140-165) - exit codes

pi.dev/docs/latest is reachable but was not compared line by line with the repository copies;
the repository copies were treated as authoritative. Anything not confirmed from those files is
marked UNVERIFIED.

### Live verification (2026-10-28, pi 1.4.0, macOS)

The open questions were then checked against a real `pi` 1.4.0 (managed install, Anthropic OAuth
login, default model `anthropic/claude-opus-4-8`) and the docs bundled with that release. Results
are folded into the sections below and marked VERIFIED. In summary:

| Question | Result |
|---|---|
| `pi -p` reads the prompt from stdin | VERIFIED yes: with no positional message, `pi -p --mode json` reads redirected stdin to EOF; a multi-line prompt arrives intact as one user message. No `@file` fallback needed on macOS. |
| `session` header with `--no-session` | VERIFIED: still emitted as the first line (`{type:"session",version:3,id,timestamp,cwd}`); no session file is written. |
| Turn limit | VERIFIED none: no `--max-turns` flag or setting (only `retry.*` settings, default `retry.maxRetries` 3). Armada's timeout is the only bound. |
| Exit code on provider error | VERIFIED: a bad key gives exit 0 in `--mode json` (exit 1 in text mode). The assistant `message_end` has `stopReason:"error"` and `errorMessage` starting with the HTTP status, for example `401 {"type":"error",...}`. No auto-retry for 401. |
| SIGTERM | VERIFIED: exit 143, no `agent_end`/`agent_settled` line, and the running `bash` tool child is killed (no orphan). SIGINT exits by signal but leaves the `bash` child orphaned, so Armada must stop pi with SIGTERM or a tree kill. |
| Context-file precedence | VERIFIED: loaded agent dir, then outermost parent first, then the cwd. Per directory only one file wins: `AGENTS.override.md`, else `AGENTS.md`, else `CLAUDE.md`. A `CLAUDE.md` next to an `AGENTS.md` is NOT loaded. This changes mission-instruction delivery (see 3.7). |
| Extension `tool_call` can wait for a human | VERIFIED: a handler awaited 150 s twice with no pi-side timeout; `ctx.hasUI` is false in print mode; parallel tool calls from one response are gated one at a time; `{block:true, reason}` yields `tool_execution_end` with `isError:true` and the reason as the tool result. A handler that throws blocks the tool (docs/extensions.md). |
| `--exclude-tools bash` | VERIFIED: system prompt tools become `read`, `edit`, `write`. Per `pi --help` it also applies to MCP tools. |
| `pi auth check` | VERIFIED: requires `--provider` or `--model` (exit 2 without one); exit 0 `ready`, 1 `not_ready`; `--json` prints `{"status","provider","authType"}`; `--no-refresh` avoids touching OAuth tokens. |
| Name collision / identity | `pi --version` prints a bare `1.4.0`, which does not identify the tool; the first line of `pi --help` is `pi - AI coding assistant ...` and does. |
| Windows | Still UNVERIFIED (no Windows host was used). |

## Implementation notes and deviations (Phases 1-4)

Implemented on macOS against a real pi 1.4.0. Where the implementation differs from the plan, or the plan left
a choice open, this is what was done and why:

- Detection (1.7): `HarborRuntimeDetector` gained an injectable `Verifier` that confirms a resolved `pi` with the
  first line of `pi --help` (`RuntimeDetectionService.IsPiCommand`, `PiCommandBuilder.IsPiHelpOutput`); a `pi`
  that is some other tool is advertised as unavailable. `DetectDefaultRuntime`/`DetectAllRuntimes` append pi last.
- Fixtures (2.1): captured on macOS only; no Windows host was available, so stdin delivery on Windows remains
  UNVERIFIED (noted on `PiRuntime.UsePromptStdin`). Captured verbatim (system prompt, thinking signatures and temp
  paths trimmed): plain reply, bash/write/edit tool calls, bad API key (exit 0), SIGTERM during a bash call (exit
  143). The MCP tool call, `length` stop, 429/529 errors and auto-retry fixtures are derived from those captures
  using the record shapes of pi's `docs/json.md`, because they could not be provoked on demand; this is documented
  in `PiStreamFixtures.cs`.
- Provider errors (2.4): `TryParsePiJsonLine` classifies only terminal records: `agent_end` whose `willRetry` is not
  true and whose last assistant message stopped with `error`, and `auto_retry_end{success:false}`. It does not
  classify `message_end{stopReason:error}` or `assistantMessageEvent.type=error` on their own, because pi emits the
  failed `message_end` before `agent_end{willRetry:true}` when it is about to retry, and a recovered retry must not
  be reported. pi reports a final failure in both `agent_end` and `auto_retry_end`, so `PiRuntime` and
  `RemoteAgentRuntime` raise only the first provider error of a launch.
- Log formatting (2.6): the formatter is stateless per line, so it shows the joined text that pi already puts in
  `text_end`/`thinking_end` (`content`) and drops the individual deltas.
- Server consumers (2.7): Ask/chat streaming is verified through `CaptainChatService` with a stub pi
  (`HarborInteractiveLaunchSuite.pi_chat_streams_text_tools_and_usage`); the planning-session case runs on an
  in-process Harbor because a local planning turn needs a real dock. Tool chips use pi's `durationMs`.
- MCP binding (3.1): pi's default MCP tool exposure (`codemode`) does not declare tools to the model, so the
  generated entries set `"exposure": "deferred"` (tools are loaded with `tool_search`, then called as
  `mcp__armada__<tool>`) and a `description` that also marks the file as Armada-generated. Verified end to end with
  pi 1.4.0 and a live Admiral: `tool_search` then `mcp__armada__status` succeeded with the token from
  `${ARMADA_MCP_TOKEN}`.
- MCP binding (3.1/3.2): pi writes its project file even for mission launches with
  `McpAllowWorkingDirectoryFiles=false` (the other runtimes avoid dock files there), because pi has no other way to
  carry the token. `IsolationConfigFile` gained `ExcludeFromGit`, `RemoveAfterExit` and `OriginalContents`;
  `BaseAgentRuntime` adds the file to the worktree's `info/exclude` before writing it and removes it (or restores a
  repository's own `.pi/mcp.json`, whose servers are merged into the generated file) before `OnProcessExited`, and
  on a failed start. Crash leftovers: orphan recovery (`AdmiralService.RecoverOrphanedMissionAsync`) calls
  `PiProjectMcpConfig.RemoveGenerated`, which deletes only an Armada-generated file. That helper is unit tested;
  the full orphan-recovery path with a pi mission was not exercised by a test in Phases 1-4; `Services.PiCrashRecovery`
  now covers it (see "Phase 5 and gap closure").
- Permissions (3.3): no code change in `CliPermissionPolicyResolver` beyond its doc comment in Phases 1-4 (Phase 5 then
  made pi a prompting runtime, see below). The Refuse gap is closed: Refuse now passes `--exclude-tools bash,powershell`
  (`PiCommandBuilder.ShellToolNames`), so pi's optional Windows `powershell` tool is refused too. pi's other built-in tools
  (`read`, `edit`, `write`, `grep`, `find`, `ls`) do not run commands, and `codemode` scripts reach commands only through
  those tools (pi's nested calls go through the same tool set and `tool_call` hooks). The comma-separated list was
  checked against a real pi 1.4.0 on macOS; the `powershell` tool itself exists only on native Windows (UNVERIFIED there).
- Dashboard (4.4): the approval picker's existing tooltip already says runtimes other than Claude Code, Mux 1.5+
  and API endpoints fall back to Refuse, so it was not changed. The runtime label is "pi". The runtime tooltip
  i18n key was replaced in all eight locales.
- Docs (4.5): `AGENTS.md` has no runtime list and was not changed; the `CLAUDE.md` architecture line was.
- Tests: the Phase 1 "created over REST and MCP" criterion is covered by `E2E.Captain`
  (`create_captain_all_runtimes_each_creates_successfully`) and `E2E.PersonaSurfaceParity` (`mcp_captain_pi_runtime`);
  the 2.9 instructions-file criterion by `Services.MissionPrompt` (`pi_mission_writes_agents_override_carrying_agents_md`).
  `Runtimes.PiSmoke` takes the provider from `ARMADA_TEST_PI_PROVIDER` (default `anthropic`) and the model from
  `ARMADA_TEST_PI_MODEL` (default `anthropic/claude-haiku-4-5` for anthropic) and passes `--provider` to
  `pi auth check --no-refresh`.

## Phase 5 and gap closure (mission msn_mvsj2k9t_7TXuBAhn0rz)

- Extension (5.1): `src/Armada.Runtimes/Pi/armada-permission-gate.ts`, written as plain JavaScript with JSDoc types so
  pi's loader and plain `node` (tests) both run it; `Armada.Runtimes.csproj` copies it to `Pi/` in every build output
  (Admiral, Harbor, tests) for net8.0 and net10.0. Its `tool_call` handler calls `cli_permission_prompt` over MCP JSON-RPC
  (`initialize`, then `tools/call` with the `Mcp-Session-Id`; JSON or `text/event-stream` replies) at `ARMADA_MCP_URL` with
  `X-Token: $ARMADA_MCP_TOKEN`. File tools, `tool_search`, `codemode` and `mcp__armada__*` pass without a prompt (Claude
  Code `acceptEdits` semantics); every other tool is gated, including `powershell` and repository-extension tools. pi's
  `bash` is sent as `Bash` with its `command`, so Bash permission rules match. Calls are decided one at a time in arrival
  order. Missing URL/token, an unreachable Admiral, an HTTP or JSON-RPC error, an `isError` result, an unreadable reply and
  the timeout (`ARMADA_PERMISSION_TIMEOUT_SECONDS` = prompt timeout + 120 s, so Armada expires the request first) all
  block the call.
- Launch (5.2): `CliPermissionPolicyResolver.SupportsPromptHook(Pi)` and `PromptsOverMcp(Pi)` are true, so the resolver
  no longer records `RuntimeUnsupported` for pi (a launch without a scoped token still falls back with `NoSessionToken`,
  and a Harbor without the prompt capability with `RemoteHarbor`). `CliPermissionPromptSetup.Configure` sets
  `PiRuntime.PermissionGateExtensionPath` (the copy next to the binaries, `PiRuntime.FindPermissionGateExtension`) and
  `PermissionPromptTimeoutSeconds`; without the file it returns false and the launch keeps the Refuse exclusion.
  `PiCommandBuilder` adds `--extension <path>` in place of `--exclude-tools`. `CaptainThreadMcpPlanner` now also sets
  `ARMADA_MCP_URL` for pi launches (the Harbor job runner already did). A Harbor runs its own copy of the extension; an
  older Harbor without it runs the launch as Refuse and logs that in the job note.
- Windows prompt delivery (gap C): `PiRuntime.UsePromptFile` defaults to `OperatingSystem.IsWindows()`; then the prompt
  is written to `%TEMP%/armada/pi-prompts/prompt-<guid>.md`, passed last as `@<file>`, and deleted when the exit is
  resolved (or at the next launch). `pi -p --mode json @file` with no message was verified on macOS with pi 1.4.0 (the
  file arrives as the first user message inside `<file name=...>`); it is NOT verified on Windows.
- Crash recovery (gap D): orphan recovery of a pi mission now also removes a generated `AGENTS.override.md` (one that
  carries the `# Mission Instructions` heading); a leftover one would otherwise be read back as the dock's own
  instructions by the next generation, which then drops the repository's `AGENTS.md`. `Services.PiCrashRecovery` runs the
  whole path: real instructions generation, a real `PiRuntime` launch of the recording stub writing `.pi/mcp.json`, a
  killed process whose exit the Admiral never handled, then `AdmiralService.HealthCheckAsync`.
- Doc-derived fixtures: the MCP tool call, length stop, 429/529 and auto-retry fixtures in `PiStreamFixtures.cs` are
  labeled DOC-DERIVED (not captured) in the class summary and on each fixture.
- Tests: `Services.PiPermissionGate` (node harness against a fake MCP endpoint: allow, deny, event-stream reply,
  unreachable, timeout, two concurrent calls in order, ungated tools, missing token, error result, shipped file),
  `E2E.PiPermissionGate` (the extension against the real Admiral: the request is pending in the approvals list and as an
  Ask card; admin allow runs, deny blocks with the message), `Runtimes.PiSmoke` `real_pi_approve_in_armada_gate` (a real pi
  1.4.0 with the extension: the allowed bash call ran, the denied one did not, its reason reached the model, the run
  ended DONE with exit 0), and Pi cases in `Services.PiRuntime`, `Services.CliPermissionPolicy`,
  `Services.SecurityHardening` and `Services.CaptainThreadMcpPlanner`.

## Remaining work

- Manual verification on a real Windows host (no Windows machine was available): install through `install.ps1` or npm,
  the `@file` prompt through the npm `.cmd` wrapper, the `bash` tool with Git Bash, Refuse removing the `powershell` tool,
  and the permission gate extension loading from the Admiral/Harbor bin path.
- RPC mode (`pi --mode rpc`: steering, `get_session_stats`, `extension_ui_request` approvals): open question, out of scope.
- Session resume (`SupportsResume` with `--session-id <missionId>`): open question, out of scope.

## 1. Summary of pi and why it fits

pi is an open-source, provider-neutral terminal coding agent from Earendil Inc. Facts relevant to
Armada:

- Install: `curl -fsSL https://pi.dev/install.sh | sh` (macOS/Linux),
  `powershell -c "irm https://pi.dev/install.ps1 | iex"` (Windows), or
  `npm install -g --ignore-scripts @earendil-works/pi-coding-agent`. Requires Node.js 22.19 or
  later (`engines.node >=22.19.0`). Updated with `pi update`.
- Headless: `pi -p "query"` (print mode). `--mode text|json|rpc`. `--mode json` emits strict JSONL
  on stdout (diagnostics on stderr) and exits when the prompts are done. `--mode rpc` is a
  long-lived JSONL command/event protocol on stdin/stdout.
- SDK: `createAgentSession()` in the same npm package (TypeScript only).
- Providers: Anthropic, OpenAI, Google, Azure, Bedrock, OpenRouter, Mistral, Groq, xAI,
  DeepSeek, and OpenAI-compatible endpoints such as Ollama through `models.json`
  (`providers.<id>.{baseUrl, api, apiKey, models}`). Keys come from environment variables
  (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `AZURE_OPENAI_API_KEY` +
  `AZURE_OPENAI_BASE_URL`, `AWS_BEARER_TOKEN_BEDROCK`, ...) or `auth.json`.
- Config home: `~/.pi/agent` (override with `PI_CODING_AGENT_DIR`), holding `settings.json`,
  `models.json`, `auth.json`, `mcp.json`, `SYSTEM.md`, `APPEND_SYSTEM.md`, `extensions/`,
  `skills/`, `prompts/`. Projects have the same files under `.pi/`, loaded only after project
  trust.
- Context files: loaded from the agent dir, every parent directory and the cwd, without needing
  project trust (`-nc` disables them). Per directory only one file is used: `AGENTS.override.md`,
  else `AGENTS.md`, else `CLAUDE.md` (VERIFIED). Armada's default mission file is `CLAUDE.md`, which
  pi skips in any repository that also has `AGENTS.md` (Armada itself does), so pi needs its own
  instructions file name (see 3.7).
- MCP: built in, as the bundled extension `builtin:mcp`. Configured only through `mcp.json`
  (`{"mcpServers": {...}}`, stdio or `http`/`streamable-http` with `url` and `headers`, `${ENV}`
  expansion) or `pi mcp add`. Tools are named `mcp__<server>__<tool>`. `--no-mcp` disables it.
- Permissions: no approval prompts and no sandbox by default. Gating is done by extensions that
  handle the `tool_call` event and return `{ block: true, reason }`.
- Sessions: JSONL tree files under `~/.pi/agent/sessions/`; resume with `--session <path|id>`,
  pre-assign with `--session-id <id>`, or disable with `--no-session`.

Why it fits: pi's headless contract is the same shape Armada already drives for OpenCode, Codex
and Claude Code - one process per launch, prompt in, JSONL events out, exit. It reports exact
per-message token usage and cost, has first-class MCP over HTTP with header injection (so the
Armada MCP server and the session token work), exposes reasoning effort as a flag with a
superset of Armada's levels, and supports many providers, including local models, through one
binary. That gives Armada another provider-neutral captain next to OpenCode, with richer
telemetry.

## 2. Capability matrix

Values for existing runtimes come from the current adapters (`src/Armada.Runtimes/*Runtime.cs`,
`src/Armada.Core/Services/ReasoningEffortTranslator.cs`,
`src/Armada.Core/Services/CliPermissionPolicyResolver.cs`,
`src/Armada.Core/Services/MissionStreamDecoder.cs`,
`src/Armada.Core/Services/ChatTurnTelemetryRecorder.cs`). "Mission stream" means
`MissionStreamDecoder.SupportsRuntime` decodes it today.

| Capability | Claude Code | Codex | Gemini | Cursor | Mux | OpenCode | pi (proposed) |
|---|---|---|---|---|---|---|---|
| Headless mode | `claude -p` | `codex exec` | yes | `cursor-agent` | `mux` run | `opencode run` | `pi -p --mode json` |
| Streaming JSON | stream-json | `exec --json` | text | text | JSONL protocol | `--format json` | `--mode json` JSONL |
| Mission stream decode | yes | yes | no | no | yes | no | yes (Phase 2) |
| Token telemetry | result event (+cost) | turn.completed | estimated | estimated | run_completed | step_finish (+cost) | message_end usage (+cost) |
| Armada MCP binding | isolation plan | `-c` overrides | workspace settings | project mcp.json | `--mcp-config` | `OPENCODE_CONFIG_CONTENT` | project `.pi/mcp.json` (see 3.3) |
| Bypass flag | `--dangerously-skip-permissions` | full-auto/bypass | yolo | `--force` | `--yolo` | `--auto` | default behavior (no prompts) |
| Refuse | acceptEdits | non-bypass | auto_edit | no `--force` | no `--yolo` | no `--auto` | `--exclude-tools bash` (see 3.4) |
| ApproveInArmada | yes (`--permission-prompt-tool`) | falls back to Refuse | falls back | falls back | yes (Mux 1.5+) | falls back | yes, `--extension armada-permission-gate.ts` (Phase 5 [DONE]) |
| Reasoning effort | thinking-token env | `ToCodexReasoningEffort` | none | none | `ToMuxEffort` | `--variant` minimal/high | `--thinking off..high` (1:1) |
| Model selection | `--model` | `--model` | `--model` | `--model` | endpoint | `--model provider/id` | `--model provider/id` (+`--provider`) |
| Resume (`SupportsResume`) | true | false | false | false | false | false | false at first; `--session-id` makes true possible |
| Prompt via stdin | yes | yes | yes | yes | n/a | yes | yes (VERIFIED on macOS; see 3.1) |
| Mission instructions file | CLAUDE.md | CODEX.md | GEMINI.md | CURSOR.md | MUX.md | CLAUDE.md | AGENTS.override.md (see 3.7) |
| Windows | yes | yes | yes | yes | yes | yes (npm .cmd) | yes, install.ps1; bash tool needs Git Bash |

## 3. Design

### 3.1 Command building

New `src/Armada.Core/Services/PiCommandBuilder.cs` (NEW), modeled on
`src/Armada.Core/Services/OpenCodeCommandBuilder.cs`:

```
pi -p --mode json --no-session [--approve]
   [--model <provider/id>] [--thinking <level>]
   [--exclude-tools bash]          (Refuse policy only)
   [--append-system-prompt <text>] (only if Armada needs it; AGENTS.md already reaches pi)
```

- Working directory: pi has no `--cwd`/`--dir` flag; the process working directory decides
  context files, project config and `@path` resolution. `BaseAgentRuntime` already sets
  `ProcessStartInfo.WorkingDirectory`, so the builder takes no directory argument (unlike
  OpenCode's `--dir`). It still validates `workingDirectory` and `prompt` and throws
  `ArgumentNullException` like the OpenCode builder.
- Model: pass `Captain.Model` as `--model` unchanged (pi accepts `provider/id` and a
  `:<thinking>` suffix). `--thinking` overrides the suffix, so effort stays authoritative.
- Reasoning effort: new `ReasoningEffortTranslator.ToPiThinkingLevel` in
  `src/Armada.Core/Services/ReasoningEffortTranslator.cs`: Off->`off`, Minimal->`minimal`,
  Low->`low`, Medium->`medium`, High->`high`, null->omit. pi clamps to what the model supports.
  pi's extra `xhigh`/`max` are not reachable from Armada and need no mapping.
- Sessions: `--no-session` for missions, Ask turns and planning. Planning sessions relaunch
  against Armada's preserved transcript (see `AgentRuntimeCapabilities`), so pi session files are
  not needed and would otherwise accumulate in `~/.pi/agent/sessions`. VERIFIED: with
  `--no-session` the JSON `session` header is still the first line and no session file is
  written. The parser still must not depend on the header.
- Prompt delivery: every CLI adapter except Mux sets `UsePromptStdin => true` to avoid Windows
  cmd.exe truncating multi-line arguments to npm `.cmd` wrappers. VERIFIED (macOS): with no
  positional message, `pi -p` reads redirected stdin to EOF and a multi-line prompt arrives
  intact as one user message. `PiRuntime` sets `UsePromptStdin => true` and passes no message
  argument. The `@<file>` fallback is needed only if the Windows check in task 2.1 fails.
- Turn limit: VERIFIED none. pi 1.4.0 has no `--max-turns` flag or setting; the only related
  settings are `retry.*` (default `retry.maxRetries` 3, provider errors only). Armada's existing
  mission timeout and `StopAsync` remain the only bound.
- Stopping: VERIFIED that SIGTERM ends pi with exit 143 and kills the running `bash` tool child.
  SIGINT leaves that child orphaned. `BaseAgentRuntime` already sends SIGTERM and falls back to
  `Process.Kill(entireProcessTree: true)`, so no change is needed; never stop pi with SIGINT. A
  stopped run has no `agent_end`/`agent_settled`, which 3.2 already treats as a failure.

### 3.2 Stream parsing

`--mode json` record types (from `docs/json.md`):

- `session` header `{type, version, id, timestamp, cwd}` (first line; VERIFIED still emitted
  with `--no-session`, `version` 3 in pi 1.4.0).
- `agent_start`, `agent_end{messages, willRetry}`, `agent_settled{aborted}` (true end of run).
- `turn_start`, `turn_end{message, toolResults}`.
- `message_start{message}`, `message_update{usage, assistantMessageEvent}`,
  `message_end{message}` (authoritative final message). `assistantMessageEvent.type` is
  `text_start|text_delta|text_end`, `thinking_*`, `toolcall_start{id,toolName}`,
  `toolcall_delta`, `toolcall_end{toolCall}`, `done`, `error{reason,error}`.
- `tool_execution_start{toolCallId,toolName,args}`, `tool_execution_update`,
  `tool_execution_end{toolCallId,toolName,result,isError,durationMs}`.
- `auto_retry_start{attempt,maxAttempts,delayMs,errorMessage}`,
  `auto_retry_end{success,attempt,finalError?}`, `compaction_start/end`, `queue_update`, and
  others that Armada ignores.
- Usage: `{input, output, cacheRead, cacheWrite, cacheWrite1h?, reasoning?, totalTokens,
  cost:{input,output,cacheRead,cacheWrite,total}}`. `reasoning` is already included in `output`.
  On `message_update` it is cumulative for the current response and may be zero until completion;
  on `message_end` it is final.
- Assistant message: `stopReason` in `pending|stop|length|toolUse|error|aborted|deferred`, plus
  `errorMessage?`, `provider`, `model`, `responseModel?`.

Framing rule: split on LF only and strip a trailing CR (pi documents that U+2028/U+2029 are not
record boundaries). Armada's line reader in `BaseAgentRuntime` already reads by line;
`RuntimeStreamJson` must accept the line as a single JSON object.

Typed models, following the repository rule of no `JsonElement` access and one class per file
(`src/Armada.Core/Protocol/OpenCodeStreamEvent.cs` and friends are the pattern):

- `src/Armada.Core/Protocol/PiStreamEvent.cs` (NEW) - top-level record; `TryParse(line, out evt)`
  built on `src/Armada.Core/Protocol/RuntimeStreamJson.cs`; constants for the type names.
- `src/Armada.Core/Protocol/PiAssistantMessageEvent.cs` (NEW) - nested delta event.
- `src/Armada.Core/Protocol/PiMessage.cs` (NEW) - role, content blocks, stopReason,
  errorMessage, provider, model, usage.
- `src/Armada.Core/Protocol/PiContentBlock.cs` (NEW) - text / thinking / toolCall blocks (use
  `src/Armada.Core/Protocol/TextOrRawJsonConverter.cs` or `RawJsonStringConverter.cs` for
  free-form `args` and `result`).
- `src/Armada.Core/Protocol/PiUsage.cs` and `src/Armada.Core/Protocol/PiCost.cs` (NEW).

Consumers:

- Final message: pi in JSON mode exits 0 even on provider errors (`print-mode.ts` sets exit 1 for
  error/aborted/length only in text mode; VERIFIED with a bad key: exit 0, last assistant
  `message_end` has `stopReason:"error"` and `errorMessage` starting `401 {...}`). `PiRuntime` therefore keeps the last assistant
  `message_end` and, in `ResolveExitCode`, writes its text to the final-message file and returns a
  nonzero code when the last `stopReason` is `error`, `aborted` or `length`, or when the stream
  ended without `agent_end`/`agent_settled` (EOF). A new helper
  `src/Armada.Runtimes/PiFinalMessage.cs` (NEW) replaces the
  `src/Armada.Runtimes/CliTextFinalMessage.cs` role used by OpenCode.
- Provider errors: `RuntimeProviderErrorParser.TryParsePiJsonLine` (new method in
  `src/Armada.Core/Services/RuntimeProviderErrorParser.cs`) classifies `message_end` with
  `stopReason=error`, `assistantMessageEvent.type=error`, and `auto_retry_end{success:false}` using
  the existing explicit-signal rules (HTTP status in `errorMessage`, never substring guessing on
  narration). Stderr keeps `TryParseCliStderrLine(line, "Pi")`. A retry that later succeeds
  (`auto_retry_start` then normal completion) must not be reported as a failure.
- Telemetry: `ChatTurnTelemetryRecorder.ObservePi` sums usage from each assistant `message_end`
  (one per model response; a turn with tool calls has several). It must not add `reasoning` to
  output again and must not sum `message_update` usage (cumulative, would double count).
- Log formatting: `RuntimeLogFormatter.TryFormatPiEvent` shows `text_delta` joined text,
  `tool_execution_start` as a tool line, `tool_execution_end` with `isError`, and drops
  `message_update` usage noise.
- Mission streaming: `src/Armada.Core/Services/PiMissionStreamDecoder.cs` (NEW), registered in
  `MissionStreamDecoder.SupportsRuntime` / factory, modeled on `MuxMissionStreamDecoder.cs`
  (join deltas into lines).
- Tool wait tracking: `RuntimeToolWaitTracker` gains a pi branch keyed on
  `tool_execution_start` / `tool_execution_end` `toolCallId`.
- Ask tool calls: `src/Armada.Server/Ask/ToolCallCollector.cs` can use pi's `durationMs` instead
  of the first-seen clock used for Codex/OpenCode.

### 3.3 MCP config injection

pi has no per-invocation MCP flag (no `--mcp-config`, no config-content environment variable).
It reads `~/.pi/agent/mcp.json` and, for trusted projects, `.pi/mcp.json`, where a project entry
replaces a user entry of the same name. Header values support `${ENV}` expansion.

Recommendation (same strategy as Cursor's project `.cursor/mcp.json` in
`CaptainThreadMcpPlanner`):

- Write `<workingDirectory>/.pi/mcp.json` containing `armada` (and every host Armada alias found
  in `~/.pi/agent/mcp.json`, redefined as the scoped server or `"enabled": false`) with
  `"type": "http"`, the Admiral MCP URL, and `"headers": {"X-Token": "${ARMADA_MCP_TOKEN}"}`. The
  token travels only in `CaptainThreadMcpPlanner.TokenEnvironmentVariable`, never on disk or argv.
- Pass `--approve` so the headless run trusts the project and loads `.pi/mcp.json` (headless runs
  otherwise skip untrusted project config, `defaultProjectTrust` "ask").
- For Ask turns the working directory is the throwaway turn directory, so nothing leaks. For
  missions the directory is a dock (git worktree): add `.pi/mcp.json` to the worktree's
  `info/exclude` before launch so it is never committed, and delete it after exit.
- `PiRuntime.PopulateHostMcpConfiguration` reads `~/.pi/agent/mcp.json` (or
  `$PI_CODING_AGENT_DIR/mcp.json`) into a new `CaptainThreadMcpPlanRequest.HostPiMcpConfigDocuments`
  list; `HostMcpConfigDocument` already models an `mcpServers` map.
- Isolation: `CaptainLaunchIsolationPlanner.CarriesSessionToken(Pi)` returns false (like
  OpenCode), so full-isolation token launches fall back to the per-invocation plan above.

Alternative considered: a per-launch `PI_CODING_AGENT_DIR` overlay with copied
`settings.json`/`models.json` and a generated `mcp.json`. Rejected for now because `auth.json`
holds OAuth tokens that pi rewrites on refresh; copying it loses refreshes and symlinking it is
UNVERIFIED (an atomic rename would break the link). It hides the user's login, which
`CaptainThreadMcpPlanner` explicitly avoids.

Risk of `--approve`: it also loads the repository's own `.pi/extensions` and `.pi/settings.json`.
A captain already runs repository code through bash, so this is not a new trust boundary, but it
must be documented in `docs/INSTRUCTIONS_FOR_PI.md` (NEW).

### 3.4 Permission policy mapping

pi never prompts; there is no flag to turn prompts on. Mapping of `CliPermissionPolicyEnum`:

- Bypass: default pi behavior (all built-in tools). This matches `--auto`/`--yolo` elsewhere and
  stays admin-only through `CliPermissionService`.
- Refuse: Claude Code's Refuse is `acceptEdits` in print mode - file edits are allowed, shell and
  other prompting tools are refused. The pi equivalent is `--exclude-tools bash` (keeps `read`,
  `edit`, `write` and MCP tools; `--tools` would drop MCP tools only if an entry starts with
  `mcp__`, which Armada will not pass). Captain `AutoApprove=false` maps to Refuse, as for OpenCode.
- ApproveInArmada: Phases 1-4 left `CliPermissionPolicyResolver.SupportsPromptHook(Pi)` and
  `PromptsOverMcp(Pi)` false, so the resolver's `ApplyFallback` downgraded to Refuse with
  `CliPermissionFallbackReasonEnum.RuntimeUnsupported`. Phase 5 [DONE] made both true (see "Phase 5 and gap closure").
- Phase 5 [DONE]: ship an Armada extension, `src/Armada.Runtimes/Pi/armada-permission-gate.ts`
  (NEW), loaded with `--extension <path>`. Its `tool_call` handler calls the Armada MCP tool
  `cli_permission_prompt` over the same scoped MCP connection (or the REST equivalent with
  `ARMADA_MCP_TOKEN`), awaits the decision, and returns `{ block: true, reason }` on deny. Then
  `SupportsPromptHook`/`PromptsOverMcp` return true for pi and `CliPermissionPromptSetup` gets a pi
  branch. VERIFIED: an async `tool_call` handler awaited 150 s twice with no pi-side timeout, so
  the wait is bounded only by the extension's own timeout (use the Armada approval timeout) and
  Armada's mission timeout. In print mode `ctx.hasUI` is false. Parallel tool calls from one
  model response reach the handler one at a time, so one pending approval holds the others.
  Returning `{ block: true, reason }` produces `tool_execution_end` with `isError: true` and the
  reason as the tool result, which the model sees. A handler that throws also blocks the tool. The
  extension must still fail closed (block) on any error, as
  `examples/extensions/permission-gate.ts` does when `ctx.hasUI` is false.

Armada's own MCP tools stay governed by Ask proposals and per-tool authorization regardless of
the CLI policy.

### 3.5 Harbor detection

- `src/Armada.Core/Harbor/HarborRuntimeDetector.cs`: `CommandFor(Pi) => "pi"`.
- `src/Armada.Core/Services/RuntimeDetectionService.cs`: add `pi` to `DetectAllRuntimes` and the
  install hint ("Install pi (https://pi.dev) and ensure the `pi` command is on PATH"). Note that
  OpenCode is missing from this service today; adding pi should not silently change the default
  order of `DetectDefaultRuntime` (append pi last).
- `src/Armada.Core/Models/HarborCapability.cs`: doc comment examples list "pi".
- Optional readiness: VERIFIED `pi auth check --provider <p>` (or `--model <provider/id>`) exits
  0 `ready`, 1 `not_ready`, 2 when neither option is given. `--json` prints
  `{"status","provider","authType"}`; `--no-refresh` avoids refreshing OAuth tokens and should
  always be passed by Armada. Not required for detection; candidate for a later doctor/health
  check and for the smoke-suite skip check.
- Name collision risk: `pi` is a short command name. `pi --version` prints a bare version (`1.4.0`)
  that does not identify the tool, so detection confirms with the first line of `pi --help`,
  which starts `pi - AI coding assistant`. Treat any other output as "not pi".

### 3.6 Print + JSON mode versus RPC mode

Recommendation: use print + `--mode json` for every Armada launch (missions, Ask turns, planning).

- It matches `BaseAgentRuntime`'s one-process-per-launch lifecycle, process ledger, `StopAsync`,
  Harbor remote execution (`RemoteAgentRuntime`, `LocalHarborJobRunner`) and the existing stdout
  line pipeline. No new long-lived process management is needed.
- The event shapes are the same as RPC mode, so the parser written now is reusable.
- RPC mode adds steering, `abort`, `get_session_stats`, and `extension_ui_request` (a possible
  native approval bridge with `ctx.hasUI=true`). Those matter only for a future interactive or
  steerable captain; revisit RPC if Armada adds mid-mission steering or if the Phase 5 extension
  needs `ctx.ui` dialogs. Track as an open question, not in this plan's scope.
- The SDK is TypeScript-only and would require a Node sidecar; not recommended.

### 3.7 Mission instructions file

VERIFIED: pi loads context files from the agent dir, then every parent directory from the
outermost in, then the cwd. In each directory only one file is used: `AGENTS.override.md`, else
`AGENTS.md`, else `CLAUDE.md`. A `CLAUDE.md` next to an `AGENTS.md` is not loaded.

Armada writes mission instructions to the file named by
`MissionPromptBuilder.GetInstructionsFileName` (`src/Armada.Core/Services/MissionPromptBuilder.cs`),
which today falls back to `CLAUDE.md` for unknown runtimes. For pi that file would be silently
ignored in any repository that has `AGENTS.md`, including Armada itself. Changes:

- `GetInstructionsFileName("Pi")` returns `AGENTS.override.md`. pi always prefers it, so mission
  instructions are loaded in every dock.
- Because the override hides the repository's own `AGENTS.md` in the dock root, pi must take the
  existing non-Codex fallback in `src/Armada.Core/Services/MissionService.Instructions.cs`
  (around line 247), which carries the repository's `AGENTS.md`, else `CLAUDE.md`, into the
  generated file. Do not add Pi to `runtimeReadsAgentsMd`.
- If a repository already commits an `AGENTS.override.md`, the existing "preserve and prepend"
  path keeps it.
- Add `AGENTS.override.md` to `_IgnoredMissionArtifactFiles` in
  `src/Armada.Core/Services/MissionService.cs` so the generated file is not treated as mission
  output.
- The `--append-system-prompt` line in 3.1 stays unused.

## 4. Ordered task list

Each phase is sized to be one Armada mission. Tasks inside a phase are in order. Every phase
lands its own tests (listed under "Tests" in the phase and detailed in section 5); a phase is not
done until its acceptance criteria and the listed test suites pass.

### Phase 1 [DONE] - Runtime enum, adapter and command line (headless missions work)

1.1 [DONE] Add `Pi` to `src/Armada.Core/Enums/AgentRuntimeEnum.cs` with `[EnumMember(Value = "Pi")]`,
    appended after `Custom`. Captain, persona-priority, stage-outcome and CLI-permission rows
    store the runtime as its name (`Runtime.ToString()` / `Enum.Parse`, for example
    `src/Armada.Core/Database/Postgresql/Implementations/CaptainMethods.cs`), so no migration is
    needed; appending last also keeps any ordinal use (for example a stable sort) unchanged.
    Acceptance: a Pi captain round-trips through the database suites on SQLite.
1.2 [DONE] `src/Armada.Core/Services/AgentRuntimeParser.cs`: accept `pi`, `Pi`, `pi-coding-agent`;
    update the help string. Acceptance: parser unit cases for each alias; unknown values still
    rejected.
1.3 [DONE] `src/Armada.Core/Services/ReasoningEffortTranslator.cs`: add `ToPiThinkingLevel`.
    Acceptance: all five levels plus null.
1.4 [DONE] Create `src/Armada.Core/Services/PiCommandBuilder.cs` (NEW) per section 3.1. Acceptance:
    exact argument lists for default, model+effort, Refuse (`--exclude-tools bash`), empty
    working directory or prompt throws.
1.5 [DONE] Create `src/Armada.Runtimes/PiRuntime.cs` (NEW) deriving `BaseAgentRuntime`: `Name => "Pi"`,
    `SupportsResume => false`, `RuntimeType => Pi`, `GetCommand` via `ResolveExecutable`,
    `BuildArguments`, prompt delivery per the 3.1 decision, `AutoApprove`/`ExecutablePath` like
    `OpenCodeRuntime.cs`. Register in `src/Armada.Runtimes/AgentRuntimeFactory.cs`.
1.6 [DONE] `src/Armada.Core/Services/AgentRuntimeCapabilities.cs`: add Pi to `PlanningSessionRuntimes`.
1.7 [DONE] `src/Armada.Core/Harbor/HarborRuntimeDetector.cs` and
    `src/Armada.Core/Services/RuntimeDetectionService.cs` per 3.5;
    `src/Armada.Core/Models/HarborCapability.cs` doc comment.
1.8 [DONE] Runtime switches that must include Pi: `src/Armada.Runtimes/RemoteAgentRuntime.cs` (provider
    error routing, temporary stderr-only like OpenCode),
    `src/Armada.Core/Services/CaptainTierSelector.cs` (Pi defaults like OpenCode),
    `src/Armada.Core/Services/HandoffReaderResolver.cs` (Pi reads files and has MCP),
    `src/Armada.Core/Services/CaptainLaunchIsolationPlanner.cs` (`CarriesSessionToken` false, doc
    comment), `src/Armada.Core/Enums/CliPermissionPolicyEnum.cs` and
    `src/Armada.Core/Models/CaptainApprovalOptions.cs` doc comments.
Acceptance for the phase: `dotnet build src/Armada.sln` clean; a captain with runtime `Pi` can be
created over REST and MCP; a mission with a stub `pi` executable launches with the expected
arguments, prompt on stdin and `--no-session`.
Tests: new `Services.PiRuntime` suite (builder, effort mapping, parser aliases); Pi cases in
`AgentRuntimeFactorySuite`, `HarborRuntimeDetectionSuite`, `CaptainTierSuite`,
`CaptainModelSuite`, `HarborDatabaseSuite`; the stub `pi` in `StubAgentProcesses.cs` (argv,
stdin and environment recording only).

### Phase 2 [DONE] - JSON stream parsing, final message, errors, telemetry

2.1 [DONE on macOS; Windows NOT verified, no Windows host] Run a real `pi -p --mode json` on macOS and Windows; capture fixtures for: plain reply, a
    turn with `bash` + `edit` tool calls, an MCP tool call, a provider auth error (bad key), an
    abort (SIGTERM), a `length` stop, and an auto-retry. stdin prompt delivery is VERIFIED on
    macOS (3.1); confirm it on Windows and switch to `@file` there only if it fails.
    Acceptance: fixtures committed with the pi version recorded; Windows stdin result noted in
    `PiRuntime`.
2.2 [DONE] Create the protocol models in `src/Armada.Core/Protocol/` (NEW `PiStreamEvent.cs`,
    `PiAssistantMessageEvent.cs`, `PiMessage.cs`, `PiContentBlock.cs`, `PiUsage.cs`,
    `PiCost.cs`); update the doc comment in `src/Armada.Core/Protocol/RuntimeStreamJson.cs`.
    Acceptance: every fixture line parses or is ignored; malformed JSON and non-object values
    return false.
2.3 [DONE] Create `src/Armada.Runtimes/PiFinalMessage.cs` (NEW) and wire `BeginLaunch`,
    `ResolveExitCode`, `TryParseProviderError` in `PiRuntime`. Acceptance: final-message file
    holds the last assistant text; `stopReason` error/aborted/length and missing `agent_end`
    produce a nonzero code even though pi exited 0.
2.4 [DONE] `src/Armada.Core/Services/RuntimeProviderErrorParser.cs`: `TryParsePiJsonLine`; route stdout
    lines for Pi through it in `PiRuntime` and `src/Armada.Runtimes/RemoteAgentRuntime.cs`.
    Acceptance: 401/429/5xx fixtures classify correctly; a recovered auto-retry does not.
2.5 [DONE] `src/Armada.Core/Services/ChatTurnTelemetryRecorder.cs` (`ObservePi`) and
    `src/Armada.Core/Models/CaptainChatMetrics.cs` doc comment;
    `src/Armada.Core/Services/MissionService.Completion.cs` uses the runtime usage report for Pi.
    Acceptance: multi-response fixture sums input/output/cache and cost exactly; reasoning not
    double counted.
2.6 [DONE] `src/Armada.Core/Services/RuntimeLogFormatter.cs` (`TryFormatPiEvent`).
2.7 [DONE] Server consumers: `src/Armada.Server/CaptainChatService.cs` (Ask streaming: text deltas,
    thinking, tool use, telemetry), `src/Armada.Server/PlanningSessionCoordinator.cs` (strip
    protocol lines, keep assistant text; add `PiRuntime.IsProtocolEventLine` /
    `TryExtractAssistantText` helpers like OpenCode's), `src/Armada.Server/Ask/ToolCallCollector.cs`
    (`durationMs`).
2.8 [DONE] `src/Armada.Core/Services/MissionStreamDecoder.cs` plus
    `src/Armada.Core/Services/PiMissionStreamDecoder.cs` (NEW);
    `src/Armada.Core/Services/RuntimeToolWaitTracker.cs` pi branch.
2.9 [DONE] Mission instructions per 3.7: `MissionPromptBuilder.GetInstructionsFileName` maps `Pi` to
    `AGENTS.override.md`; `MissionService.cs` adds it to `_IgnoredMissionArtifactFiles`.
    Acceptance: in a dock with `AGENTS.md`, the generated `AGENTS.override.md` holds the mission
    instructions followed by the repository's `AGENTS.md`; a stub-pi launch shows the file in the
    dock; `git status` is clean after the mission.
Acceptance for the phase: a stub-pi mission replaying the 2.1 fixtures completes with the right
final message, exit code, token counts and cost; a replay of the bad-key fixture fails the mission
with a classified provider error even though the stub exits 0; an Ask turn on a pi captain
streams text and tool calls in the dashboard; planning-session output contains no protocol lines.
Tests: `PiStreamFixtures.cs` plus the stream, final-message and exit-code cases in
`Services.PiRuntime`; Pi cases in `RuntimeProtocolParsingSuite`,
`RuntimeProviderErrorParserSuite`, `ChatTurnTelemetryRecorderSuite`, `MissionStreamingSuite`;
the stub `pi` gains fixture replay.

### Phase 3 [DONE] - MCP binding and permission policy

3.1 [DONE] `src/Armada.Core/Services/CaptainThreadMcpPlanner.cs`: Pi strategy per 3.3 (project
    `.pi/mcp.json`, alias disabling, `--approve`); `src/Armada.Core/Services/CaptainThreadMcpPlanRequest.cs`:
    `HostPiMcpConfigDocuments`. `src/Armada.Core/Models/HostMcpConfigDocument.cs` doc comment.
3.2 [DONE] `PiRuntime.PopulateHostMcpConfiguration` reads `~/.pi/agent/mcp.json` /
    `$PI_CODING_AGENT_DIR/mcp.json`; mission launches add `.pi/mcp.json` to the worktree exclude
    file and remove it after exit (hook location in `src/Armada.Runtimes/BaseAgentRuntime.cs`).
    `src/Armada.Core/Settings/McpSettings.cs` doc comment.
3.3 [DONE] `PiCommandBuilder` Refuse mapping and `src/Armada.Core/Services/CliPermissionPolicyResolver.cs`
    (no prompt hook, so ApproveInArmada falls back to Refuse with RuntimeUnsupported).
    `src/Armada.Runtimes/CliPermissionPromptSetup.cs` unchanged until Phase 5.
Acceptance: a thread-scoped Ask turn on a pi captain can call `mcp__armada__status` with the
session token; the token is not in argv or on disk; a host `armada` entry in
`~/.pi/agent/mcp.json` is not used; the dock's `git status` is clean after the mission; a
captain with `AutoApprove=false` launches with `--exclude-tools bash`; ApproveInArmada is
recorded as falling back to Refuse with `RuntimeUnsupported` and the note is shown to the user.
Tests: `PiMcpConfigFile.cs` helper; Pi cases in `CaptainThreadMcpPlannerSuite`,
`CliPermissionPolicySuite`, `ThreadScopedLaunchSuite`, `SecurityHardeningSuite` (token never in
argv), `McpClientHostSuite`; a crashed-launch case proving orphan recovery deletes `.pi/mcp.json`.

### Phase 4 [DONE] - Clients, CLI, TUI, dashboard, docs

4.1 [DONE] Helm: `src/Armada.Helm/Commands/McpClientKindEnum.cs` (`Pi`),
    `src/Armada.Helm/Commands/McpConfigHelper.cs` (`GetPiConfigPath` = `~/.pi/agent/mcp.json`
    honoring `PI_CODING_AGENT_DIR`, `IsPiAvailable`, local and remote targets writing
    `mcpServers.armada`), `src/Armada.Helm/Commands/McpInstallCommand.cs` (target list and plain-
    text-token note), `src/Armada.Helm/Commands/CaptainAddSettings.cs` and
    `src/Armada.Helm/Commands/CaptainUpdateSettings.cs` (runtime help text).
4.2 [DONE] TUI: `src/Armada.Tui/Screens/Build/CaptainForm.cs` (`Runtimes`, `SupportsAutoApprove`,
    label), `src/Armada.Tui/Ask/AskController.cs` (`INSTRUCTIONS_FOR_PI.md`).
4.3 [DONE] Server MCP schemas: `src/Armada.Server/Mcp/Tools/McpCaptainTools.cs` and
    `src/Armada.Server/Mcp/Tools/McpStageOutcomeTools.cs` runtime descriptions.
4.4 [DONE] Dashboard: `src/Armada.Dashboard/src/lib/captainForm.ts` (option and tooltip),
    `src/Armada.Dashboard/src/lib/captainApproval.ts`, `src/Armada.Dashboard/src/lib/askCaptain.ts`,
    `src/Armada.Dashboard/src/pages/Captains.tsx`, `src/Armada.Dashboard/src/pages/CaptainDetail.tsx`,
    `src/Armada.Dashboard/src/types/models.ts` (cost comment). The tooltip string is an i18n key:
    update it in all eight locales of `src/Armada.Server/wwwroot/i18n/armada.json` in the same
    commit, or the catalog tests fail.
4.5 [DONE] Docs: `docs/INSTRUCTIONS_FOR_PI.md` (NEW) and `docs/PI_AS_ORCHESTRATOR.md` (NEW) modeled on
    the OpenCode pair; runtime lists in `docs/CAPTAINS.md`, `docs/CAPTAIN_ROUTING.md`,
    `docs/MCP_API.md`, `docs/REST_API.md`, `docs/MODEL_GUIDANCE.md`, `docs/MODEL_HETEROGENEITY.md`,
    `docs/HARBOR_AWARE_TOOL_PROBE.md`, `docs/REMOTE_SERVER.md`, `docs/SECURITY_REVIEW.md`,
    `docs/TESTING.md`, `docs/UPGRADING.md`, `docs/WEBSOCKET_API.md`, `docs/ASK_ARMADA_HOME_BASE.md`;
    `README.md`, `GETTING_STARTED.md`, `FAST_TRACK_SETUP.md`, `DOCKERHUB_README.md`,
    `CLAUDE.md` / `AGENTS.md` architecture line, and `CHANGELOG.md` (`## Unreleased`).
4.6 [DONE] `Armada.postman_collection.json`: the captain create description lists runtimes (and already
    omits Mux and OpenCode); add them and Pi. Edit as bytes (the file is CRLF with LF blocks).
4.7 [DONE] API surface: MCP schema description changes may alter `docs/api-surface-1.0.json` /
    `docs/API_SURFACE_1.0.md`; regenerate with `scripts/common/generate-api-surface.sh -f net10.0`
    if E2E.ApiContract reports a change.
4.8 [DONE] Installers / Harbor app: no runtime is bundled by the installers today (grep of `scripts`,
    `docker`, `factory`, `publisher.json` finds no runtime CLI installation), so nothing to add;
    confirm the Harbor tray's runtime list comes from `HarborRuntimeDetector`.
Acceptance for the phase:
- `armada mcp install` lists Pi when `pi` is on PATH (and not otherwise), writes an `armada`
  entry under `mcpServers` in `~/.pi/agent/mcp.json` (or `$PI_CODING_AGENT_DIR/mcp.json`) while
  keeping existing entries, and works for both local and remote targets; the plain-text-token
  note names Pi.
- `armada captain add --runtime pi` and `captain update` accept Pi and the help text lists it.
- The TUI captain form offers Pi, and auto-approve is available for it; the TUI Ask screen loads
  `docs/INSTRUCTIONS_FOR_PI.md` for a pi captain.
- The dashboard captain form, captain list and captain detail show Pi; the approval picker shows
  ApproveInArmada as falling back to Refuse for Pi; the tooltip is translated in all eight
  locales.
- Every runtime list in the docs from 4.5, and the Postman description, includes Pi (and the
  Postman one also Mux and OpenCode); `CHANGELOG.md` has an `## Unreleased` entry.
- `docs/api-surface-1.0.json` and `docs/API_SURFACE_1.0.md` match the generator output.
Tests: Pi cases in `HelmInputSuite` and `McpClientHostSuite` (config path, merge, availability);
vitest cases in `captainForm.test.ts`, `captainApproval.test.ts` and an `askCaptain` test;
gates `Tui`, `Client`, `E2E.ApiContract`, dashboard `npm run build` and `npm run test:run`
(including the `*Catalog.test.ts` i18n suites).

### Phase 5 [DONE] - ApproveInArmada through a pi extension

5.1 [DONE] `src/Armada.Runtimes/Pi/armada-permission-gate.ts` (NEW) per 3.4, copied to the bin
    (csproj item; edit the `.csproj` as bytes, CRLF).
5.2 [DONE] `CliPermissionPolicyResolver.SupportsPromptHook`/`PromptsOverMcp` return true for Pi;
    `CliPermissionPromptSetup.cs` adds `--extension <path>` to the launch.
5.3 [DONE] Tests: allow, deny, Admiral unreachable (must block), extension timeout (must block), a
    model response with two parallel tool calls (both decided, in order), and a stub-pi launch
    whose argv contains `--extension <path>` pointing at a file that exists in the bin.
Acceptance for the phase: with ApproveInArmada, a pi mission that runs `bash` raises an
approval in the dashboard and Ask; approving runs the command, denying makes the tool result an
error carrying the reason and the mission continues; if the Admiral is unreachable or the
approval times out, the command does not run; the resolver no longer records a
`RuntimeUnsupported` fallback for Pi; the extension ships in the build output on every target
framework; `docs/INSTRUCTIONS_FOR_PI.md` and the dashboard approval text no longer say Pi falls
back to Refuse.

## 5. Test plan

Test project: suites in `src/Test.Shared` (discovered by reflection), run with
`dotnet run --project src/Test.Automated --framework net10.0 -- --suites <prefix>`; the same
suites run under `src/Test.Xunit` and `src/Test.Nunit`. Dashboard tests run with vitest in
`src/Armada.Dashboard`.

Each bullet below lands with the phase named in that phase's "Tests" line (section 4).

Unit tests (no pi needed):

- `src/Test.Shared/Suites/Services/PiRuntimeSuite.cs` (NEW, suite id `Services.PiRuntime`),
  modeled on `src/Test.Shared/Suites/Services/OpenCodeRuntimeSuite.cs`: builder arguments,
  effort mapping, parser aliases, protocol-line detection, assistant text extraction, final
  message and exit-code resolution for each stopReason, EOF without `agent_end`.
- `src/Test.Shared/Infrastructure/PiStreamFixtures.cs` (NEW): verbatim captured lines from task
  2.1 (record the pi version in a comment). Example of the shape, from `docs/json.md`:
  `{"type":"message_update","usage":{"input":100,"output":1,"cacheRead":0,"cacheWrite":0,"totalTokens":101,"cost":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"total":0}},"assistantMessageEvent":{"type":"text_delta","contentIndex":0,"delta":"Hello "}}`
- Extend existing suites with Pi cases: `RuntimeProtocolParsingSuite.cs`,
  `RuntimeProviderErrorParserSuite.cs`, `ChatTurnTelemetryRecorderSuite.cs`,
  `CaptainThreadMcpPlannerSuite.cs`, `CliPermissionPolicySuite.cs` (ApproveInArmada falls back to
  Refuse; Refuse adds `--exclude-tools bash`), `HarborRuntimeDetectionSuite.cs`,
  `CaptainTierSuite.cs`, `McpClientHostSuite.cs`, `HelmInputSuite.cs`, `SecurityHardeningSuite.cs`
  (token never in argv), all under `src/Test.Shared/Suites/Services/`;
  `src/Test.Shared/Suites/Runtimes/AgentRuntimeFactorySuite.cs`,
  `src/Test.Shared/Suites/Runtimes/ThreadScopedLaunchSuite.cs`,
  `src/Test.Shared/Suites/Runtimes/MissionStreamingSuite.cs`,
  `src/Test.Shared/Suites/Models/CaptainModelSuite.cs`,
  `src/Test.Shared/Suites/Database/HarborDatabaseSuite.cs`.
- Stub executable: extend `src/Test.Shared/Infrastructure/StubAgentProcesses.cs` with a stub
  `pi` that replays fixture lines and records argv, stdin and environment, so launch tests assert
  the real command line and that `.pi/mcp.json` contains `${ARMADA_MCP_TOKEN}` rather than the
  token. Test helpers for the generated file: `src/Test.Shared/Infrastructure/PiMcpConfigFile.cs`
  (NEW), like `OpenCodeConfigFile.cs`.
- Dashboard: vitest cases for `captainForm.ts`, `captainApproval.ts`, `askCaptain.ts`; the
  existing i18n catalog tests cover the tooltip.

Integration smoke test:

- `src/Test.Shared/Suites/Runtimes/PiRuntimeSmokeSuite.cs` (NEW, suite id `Runtimes.PiSmoke`):
  skipped when `pi` is not on PATH or `pi auth check --provider <provider>` (provider from the
  test settings) is not 0. Runs a real `PiRuntime` in a temp
  git repo with prompt "create hello.txt containing hi and reply DONE", asserts exit 0, the file,
  the final message, and nonzero token counts. A second case runs with a deliberately bad API key
  (`--api-key` or env) and asserts the provider error is classified and the exit code is nonzero.
- The smoke suite lands with Phase 2 (after the fixtures exist); Phase 5 adds an ApproveInArmada case ([DONE]: `real_pi_approve_in_armada_gate`).
- Manual check per release on Windows (install.ps1, Git Bash present) since CI has no pi.

Gates to run: `Services.PiRuntime`, `Runtimes`, `Services.CliPermission`, `Services.Captain`,
`E2E.ApiContract`, `E2E.Authorization`, `Tui`, `Client`, dashboard `npm run build` and
`npm run test:run`.

## 6. Open questions and risks

- Resolved by the live check (see Sources): stdin prompt delivery (macOS), session header with
  `--no-session`, context-file precedence (now 3.7), no turn limit, SIGTERM behavior, long
  extension waits (Phase 5), `pi auth check` options, and identifying pi on PATH.
- UNVERIFIED (still open): behavior on Windows beyond `docs/windows.md`. Armada now passes the prompt as `@file` on
  Windows instead of stdin through the npm `.cmd` wrapper and Refuse also removes the optional `powershell` tool, but
  neither, nor the bash tool requiring Git Bash, has been run on a Windows host.
- Risk: a repository with `AGENTS.md` silently drops mission instructions written to `CLAUDE.md`.
  Task 2.9 must land with Phase 2, before any real pi mission runs.
- Risk: pi gates parallel tool calls one at a time in Phase 5, so a slow human approval stalls
  every other tool call in that response.
- Risk: JSON mode exits 0 on provider errors; if the exit-code override in 2.3 is missed, failed
  missions look successful.
- Risk: `--approve` loads repository-supplied `.pi/extensions` (repository code running in the
  pi process). Same trust as bash, but must be documented; alternatively offer a captain option
  to use `--no-approve` and accept no Armada MCP.
- Risk (addressed): writing `.pi/mcp.json` into a dock; the exclude entry and cleanup must be robust to
  crashes. Orphan recovery deletes it and the generated `AGENTS.override.md` (`Services.PiCrashRecovery`).
- Risk: pi auto-compaction and auto-retry change usage mid-run; telemetry tests must include
  compaction and retry fixtures.
- Risk: a slow stdout reader stalls pi (documented); Armada's reader must keep draining while
  formatting.
- Risk: `pi` command name collisions on PATH.
- Open: should Pi ever use RPC mode (steering, `get_session_stats`, `extension_ui_request`
  approvals)? Phase 5 is done without it; still open and out of scope.
- Open: `SupportsResume` could become true with `--session-id <missionId>`; out of scope until
  Armada needs resume for non-Claude runtimes.

## 7. Size estimates

| Phase | Scope | Rough size |
|---|---|---|
| 1 | Enum, parser, builder, runtime, factory, detection, switches + their tests and the argv stub | M: ~10 files changed, 3 new, 1-1.5 days |
| 2 | Protocol models, final message, errors, telemetry, log, server consumers, mission stream, instructions file + fixture tests and smoke suite | L: ~16 files, 9 new, 3 days |
| 3 | MCP binding, worktree exclude/cleanup, permission mapping + tests | M: ~7 files, 1 new, 1.5-2 days |
| 4 | Helm, TUI, MCP schema text, dashboard, i18n (8 locales), docs, Postman, API surface + Helm and vitest tests | M: ~35 files mostly text, 1.5-2 days |
| 5 | Optional permission-gate extension + tests | M: 1-2 days |
