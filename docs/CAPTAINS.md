# Captain Support Matrix

A captain is whatever actually does the work on a mission: a coding-agent CLI that Armada launches as a child
process, or an in-process loop that talks to an inference endpoint directly. Armada ships seven runtimes. They are not
equal. Claude Code gets the most attention in the code (token streaming, tool cards), Mux and
OpenCode come close because they speak structured JSON, and Codex, Gemini, and Cursor are driven as plain-text CLIs.

Everything below is read from the runtime code (`src/Armada.Runtimes`, `src/Armada.Core/Services`, and the chat and
planning coordinators in `src/Armada.Server`), not from vendor documentation. When a vendor CLI changes a flag, the
code is the thing to check first.

## Supported runtimes

| Runtime | `AgentRuntimeEnum` | Executable Armada runs | Install hint (`RuntimeDetectionService.GetInstallHint`) |
|---------|--------------------|------------------------|----------------------------------------------------------|
| Claude Code | `ClaudeCode` | `claude` | `npm install -g @anthropic-ai/claude-code` |
| Codex | `Codex` | `codex` | `npm install -g @openai/codex` |
| Gemini | `Gemini` | `gemini` | `npm install -g @google/gemini-cli` |
| Cursor | `Cursor` | `cursor-agent` | See https://docs.cursor.com/cli |
| Mux | `Mux` | `mux` | Install mux and ensure the `mux` command is on PATH |
| OpenCode | `OpenCode` | `opencode` | (see runtime documentation); not probed by `armada doctor` |
| API endpoint | `ApiEndpoint` | none (runs in the Admiral process) | Configure an inference endpoint in the dashboard |

`armada doctor` lists each runtime it finds; when it finds none it prints only the Claude Code hint.

A `Custom` value also exists in the enum. It is a hook for code that registers its own `IAgentRuntime` with
`AgentRuntimeFactory.Register`; it is not a product runtime, it cannot be used for planning or Ask, and the rest of
this document ignores it.

## Versions

Armada does not check a minimum version of any agent CLI. There is no version parsing anywhere in the runtime code.
The only detection is `RuntimeDetectionService.IsCommandAvailable`, which starts `<executable> --version` and treats
"the process started" as "installed". It does not read the output, and it does not even look at the exit code. `armada
doctor` and default-runtime selection both use it, and they probe `claude`, `codex`, `gemini`, `cursor-agent`, and `mux`
in that order. OpenCode is not in that list, so `armada doctor` never reports it; `armada mcp install` finds OpenCode
separately by looking for `~/.config/opencode` or an `opencode` executable on PATH.

In practice that means "supported version" is "a version that accepts the flags listed below". If a CLI renames or
drops one of them, the captain fails at launch and the error lands in the mission log. Pin the CLI versions on your
captain hosts and upgrade them on purpose.

## Feature matrix

| Feature | Claude Code | Codex | Gemini | Cursor | Mux | OpenCode | ApiEndpoint |
|---------|:-----------:|:-----:|:------:|:------:|:---:|:--------:|:-----------:|
| Missions | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| Planning sessions | Yes | Yes | Yes | Yes | Yes | Yes | No |
| Ask Armada threads | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| Ask approval gating | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| Streaming replies (chat and planning) | Token deltas | Line by line | Line by line | Line by line | Token deltas | JSON events | Per model response |
| Thinking display | Prompted block | Prompted block | Prompted block | Prompted block | Native | Native | Prompted block |
| Tool-call cards in chat | Yes | No | No | No | Yes | Yes | Yes |
| Model selection | `--model` | `--model` | `--model` | `--model` | `--model` | `--model` | Endpoint model, captain override |
| Reasoning effort | `MAX_THINKING_TOKENS` | `model_reasoning_effort` | No | No | `--effort` | `--variant` | No |
| Resume | Yes | No | No | No | No | No | No |
| Isolated launch (`IsolateCaptainLaunch`) | Yes | Yes | Yes | Yes | Yes | No | Not applicable |

A few of those cells need more than one word.

**Ask approval gating** is the one that matters most. Ask threads hold mutating Armada tool calls for a human to
approve unless the thread is set to auto-approve. That only works when the captain's connection to Armada's MCP server
carries a thread-scoped session token, because the token is how the server knows which thread a tool call belongs to.
ApiEndpoint captains get the token through `ARMADA_MCP_URL` and `ARMADA_MCP_TOKEN`. Every CLI runtime gets it from
`CaptainThreadMcpPlanner` for each Ask turn, using the CLI's own per-invocation override so its login stays where the
CLI expects it (no `HOME`, `CODEX_HOME`, or config-directory redirect). The token travels in the `ARMADA_MCP_TOKEN`
environment variable and is sent as an `X-Token` header; where the CLI expands environment variables in its MCP
configuration the token is not written to disk or put on the command line.

| Runtime | How an Ask turn reaches Armada with the thread token | Other Armada entries in the user's config |
|---------|------------------------------------------------------|--------------------------------------------|
| Claude Code | `--strict-mcp-config --mcp-config <per-launch file>` (token in the file, deleted when the process exits) | Ignored (strict) |
| Codex | `-c mcp_servers.<name>={ url, env_http_headers = { "X-Token" = "ARMADA_MCP_TOKEN" }, default_tools_approval_mode = "approve" }` | Disabled with `-c mcp_servers.<alias>.enabled=false`; the scoped entry is named `armada`, or `armada_ask` when the user already has an `armada` entry |
| Gemini | `.gemini/settings.json` in the turn's throwaway working directory (`httpUrl`, header `$ARMADA_MCP_TOKEN`), `GEMINI_CLI_TRUST_WORKSPACE=true`, `--allowed-mcp-server-names armada` | Not loaded (allow list) |
| Cursor | `.cursor/mcp.json` in the throwaway working directory (header `${env:ARMADA_MCP_TOKEN}`); `--force` approves it | Each alias found in `~/.cursor/mcp.json` is redefined as the scoped server |
| Mux | `--mcp-config <per-launch file> --strict-mcp-config` (API-key auth in `X-Token`, value `${ARMADA_MCP_TOKEN}`) | Ignored (strict) |
| OpenCode | `OPENCODE_CONFIG_CONTENT` merged over the user's config: a fresh remote entry (header `{env:ARMADA_MCP_TOKEN}`) | Disabled with `"enabled": false`; same naming rule as Codex |

An "alias" is any server whose name contains `armada`, whose URL targets the Admiral MCP port, or whose command runs
the `armada` CLI or `Armada.Helm`. Codex and OpenCode merge an override into an existing entry of the same name (an
`Authorization` header in the user's entry would survive and win over `X-Token`), which is why they get a fresh entry
instead. Codex needs `default_tools_approval_mode = "approve"` because `codex exec` never prompts and otherwise refuses
every MCP call; the human approval happens in the Ask thread. The Gemini trust variable applies only to the throwaway
directory (an untrusted folder loads no MCP servers at all). Verified on macOS on 2026-10-04 with real CLIs: full Ask
turns with Codex 0.159.3 (signed in), Claude Code 2.1.289, and OpenCode 1.18.34 (free `opencode/big-pickle` model)
produced a pending proposal and nothing ran until approval; Gemini CLI 0.62.0, cursor-agent 2026.10.01, and Mux 1.1.1
were verified up to the MCP handshake (the server received the thread token in `X-Token`) because no signed-in account
or model endpoint was available for them. The Codex override uses `default_tools_approval_mode`, which older Codex
releases may not accept; Mux needs 0.7.0 or newer for `--strict-mcp-config`.

**Mission-scoped tokens.** Missions use the same per-invocation binding. With `Mcp.MissionScopedTokens` on (the
default), every mission launch mints an MCP-only session token bound to the mission's tenant, owner, and captain,
valid only while the mission is Assigned or InProgress on that captain, so a mission captain calls Armada as the
mission's owner rather than as the unauthenticated loopback caller (`Mcp.AllowUnauthenticatedLoopback`, default
`true`, which only applies while the MCP listener is bound to a loopback hostname). The binding is the one in the table
above with two differences: no client files are written into the mission's worktree, so Gemini and Cursor missions get
the token only when `IsolateCaptainLaunch` is on, and with `IsolateCaptainLaunch` on the token rides in the isolated
configuration described under "How Armada launches each runtime" instead (for Mux the scoped `mcp-servers.json`
authenticates with the token as an `X-Token` API key; OpenCode has no isolated form, so it keeps the
`OPENCODE_CONFIG_CONTENT` binding from the table above). ApiEndpoint missions receive
`ARMADA_MCP_URL` and `ARMADA_MCP_TOKEN`, and Harbor launches bind the token on the Harbor. Because Claude Code's binding
is `--strict-mcp-config`, a Claude Code mission sees only Armada's MCP server, not other servers in the user's Claude
Code configuration. Set `Mcp.MissionScopedTokens` to `false` to launch missions without a token.

A `Custom` runtime is not gated, and neither is a captain on a server whose MCP listener is off. The dashboard reads
`askApprovalGated` from `GET /api/v1/captains/{id}/tools`; when it is false, the Ask conversation shows a persistent
note under the header: "Actions from this captain run without approval cards."

**Planning** runs for any CLI runtime. ApiEndpoint and Harbor-hosted (remote) runtimes report
`SupportsPlanningSessions = false`, and the coordinator refuses them. `Captain.SupportsPlanningSessions` and the
`PlanningSessionSupportReason` string come from the same per-runtime capability (`AgentRuntimeCapabilities`) that the
runtime adapters report, so an ApiEndpoint or Custom captain is shown as "planning unsupported" in the dashboard and
TUI instead of failing when the session starts, and the reason lists every supported runtime, OpenCode included.

**Streaming.** Claude Code switches to `--output-format stream-json --include-partial-messages` for Ask and interactive
planning turns, which gives real token deltas plus a clean final message and token counts from the `result` event.
Missions keep plain `--print` output. Mux streams `assistant_text` JSONL events, and OpenCode streams `--format json`
events. Codex, Gemini, and Cursor are read one stdout line at a time. The ApiEndpoint loop emits each model response
whole, between tool calls.

**Thinking.** Only Mux (`--show-thinking`, `assistant_thinking` events) and OpenCode (`--thinking`, `reasoning`
events) have a native thinking channel. For every other runtime, when the user asks to see thinking, Armada appends an
instruction to wrap reasoning in a single `<thinking>...</thinking>` block and lifts that block out of the final reply.
That is the model describing its reasoning on request, not the provider's reasoning trace.

**Tool cards** come from structured output parsed into typed events (`Armada.Core.Protocol`): Claude Code
`tool_use` and `tool_result` blocks, Mux `tool_call_proposed` and `tool_call_completed` events, OpenCode `tool_use`
parts, and the ApiEndpoint runtime's typed `OnToolEvent` channel (its stdout carries only the model's reply text, so
reply text cannot fake a tool card). A JSON line counts as a protocol event only when its discriminator (`type` or
`eventType`) is one of the runtime's known event types. Plain-text runtimes produce no cards.

**Reasoning effort** is a per-captain setting (`Off`, `Minimal`, `Low`, `Medium`, `High`) translated by
`ReasoningEffortTranslator`:

| Setting | Claude Code `MAX_THINKING_TOKENS` | Codex `model_reasoning_effort` | Mux `--effort` | OpenCode `--variant` |
|---------|-----------------------------------|--------------------------------|----------------|----------------------|
| Off | 0 | (not passed) | off | (not passed) |
| Minimal | 2048 | minimal | minimal | minimal |
| Low | 4096 | low | low | minimal |
| Medium | 8192 | medium | medium | high |
| High | 16384 | high | high | high |

Gemini, Cursor, and ApiEndpoint ignore the setting.

## How Armada launches each runtime

Every CLI runtime except Mux gets its prompt on standard input, not as an argument. On Windows these CLIs are usually npm `.cmd`
wrappers, and `cmd.exe` truncates a multi-line argument at the first newline; stdin avoids that on every platform. All
CLI launches also set `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`, because captains build .NET code
often and orphaned MSBuild nodes outlive the mission otherwise.

How a captain finds Armada's MCP server depends on `IsolateCaptainLaunch` in `settings.json` (default `false`). With it
off, the CLI uses its normal user configuration, which is what `armada mcp install` writes. With it on, Armada writes a
scoped config directory under the system temp directory (`armada/isolation/<captainId>`) that contains only the Armada
MCP server at `http://<host>:<mcpPort>/mcp` (`localhost` unless the Admiral is bound to another hostname), and points the CLI at it. Each runtime section names the mechanism.

### Claude Code

```
claude --print --verbose [--output-format stream-json --include-partial-messages] [--model <model>]
    (--dangerously-skip-permissions | --permission-mode acceptEdits --allowedTools mcp__armada)
```

The stream-json flags are added only for Ask and interactive planning turns. Without auto-approve (see "Running agents
safely") Claude Code accepts file edits, allows Armada's own MCP tools, and refuses any other tool, including shell
commands, that the project's Claude Code settings do not allow; print mode refuses rather than prompts. Armada sets
`CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT=1`, removes `CLAUDECODE` and `CLAUDE_CODE_ENTRYPOINT` so a captain can start
even when the Admiral itself was launched from inside a Claude Code session, and sets `MAX_THINKING_TOKENS` from the
captain's reasoning effort. Isolated launches add `--setting-sources project,local --strict-mcp-config --mcp-config
<dir>/armada-mcp.json`. Ask thread turns, and missions launched with a mission-scoped token, always use that strict
form with the session token as an `X-Token` header in a per-launch file, which is what makes approval gating work. Claude Code is the only runtime that reports `SupportsResume`.

Guides: [INSTRUCTIONS_FOR_CLAUDE_CODE.md](INSTRUCTIONS_FOR_CLAUDE_CODE.md),
[CLAUDE_CODE_AS_ORCHESTRATOR.md](CLAUDE_CODE_AS_ORCHESTRATOR.md).

### Codex

```
codex exec --skip-git-repo-check --sandbox workspace-write [--model <model>] [-c model_reasoning_effort=<level>] [--output-last-message <file>]
```

The approval mode defaults to `full-auto`, sent as `--sandbox workspace-write` (Codex 0.159 removed the `--full-auto`
alias from `codex exec`, which never prompts). A captain without auto-approve always gets `--sandbox workspace-write`,
on every OS. `--skip-git-repo-check` lets chat and planning turns start in their
throwaway directory, which is not a git repository. On Windows, `full-auto` is sent as
`--dangerously-bypass-approvals-and-sandbox` instead, and a `dangerous` mode sends that flag on every OS. The final reply is read from the
`--output-last-message` file. Isolated launches write `config.toml` with an `[mcp_servers.armada]` entry and set
`CODEX_HOME` to the scoped directory. When Armada validates a Codex captain it first runs `git init` in the scratch
directory, since Codex expects to run inside a git repository.

Guides: [INSTRUCTIONS_FOR_CODEX.md](INSTRUCTIONS_FOR_CODEX.md), [CODEX_AS_ORCHESTRATOR.md](CODEX_AS_ORCHESTRATOR.md).

### Gemini

```
gemini [--model <model>] --approval-mode (yolo | auto_edit)
```

`auto_edit` is used when the captain runs without auto-approve.

Gemini reads the prompt from piped stdin when no `-p` is given. Isolated launches write `.gemini/settings.json` with
an `mcpServers.armada` entry and point `HOME` and `USERPROFILE` at the scoped directory.

Guides: [INSTRUCTIONS_FOR_GEMINI.md](INSTRUCTIONS_FOR_GEMINI.md), [GEMINI_AS_ORCHESTRATOR.md](GEMINI_AS_ORCHESTRATOR.md).

### Cursor

```
cursor-agent -p [--model <model>] [--force] --output-format text
```

`--force` is omitted when the captain runs without auto-approve. Isolated launches write `.cursor/mcp.json` and override `HOME` and `USERPROFILE` the same way as Gemini. Outside
isolation, Cursor's MCP configuration is project-scoped (`.cursor/mcp.json` in the working directory), which is why the
captain tool inventory often cannot see it.

Guides: [INSTRUCTIONS_FOR_CURSOR.md](INSTRUCTIONS_FOR_CURSOR.md), [CURSOR_AS_ORCHESTRATOR.md](CURSOR_AS_ORCHESTRATOR.md).

### Mux

```
mux print [--config-dir <dir>] [--mcp-config <dir>/mcp-servers.json] [--effort <level>] [--show-thinking]
    --output-format jsonl [--output-last-message <file>] (--yolo | --approval-policy <policy>)
    [--endpoint <name>] [--model <model>] [--base-url <url>] [--adapter-type <type>]
    [--temperature <t>] [--max-tokens <n>] [--system-prompt <path>] --working-directory <dir> <prompt>
```

Mux is the most configurable runtime because its per-captain options (config directory, endpoint, base URL, adapter
type, temperature, max tokens, system prompt path, approval policy) are stored on the captain and passed through.
`--mcp-config` is added whenever `mcp-servers.json` exists in the Mux config directory (`--config-dir`, then
`MUX_CONFIG_DIR`, then `~/.mux`). An empty, `auto`, or `autoapprove` approval policy becomes `--yolo`; anything else is passed as
`--approval-policy`. With no policy set and auto-approve off, the policy is `deny`. Mux is the only runtime with a
named approval policy per captain (`muxApprovalPolicy` on the captain MCP tools); the other runtimes have only the
on/off `autoApprove` switch. Isolated launches write `mcp-servers.json` and set `MUX_CONFIG_DIR`; for a mission with a
mission-scoped token that document authenticates with the token (API-key auth in `X-Token`).
Mux is the one CLI runtime that takes the prompt as a positional argument (the last one) rather than on stdin.

Guides: [INSTRUCTIONS_FOR_MUX.md](INSTRUCTIONS_FOR_MUX.md), [MUX_AS_ORCHESTRATOR.md](MUX_AS_ORCHESTRATOR.md).

### OpenCode

```
opencode run --format json [--model <model>] [--variant <variant>] [--thinking] [--auto] --dir <workingDirectory>
```

`--auto` is omitted when the captain runs without auto-approve. OpenCode has no entry in the launch isolation planner, so `IsolateCaptainLaunch` does not isolate it (a mission with a
mission-scoped token still gets the `OPENCODE_CONFIG_CONTENT` token binding); it always reads
the user's `~/.config/opencode/opencode.json` (or `.jsonc`), where `armada mcp install` writes a `remote` entry under
`mcp`. It is also missing from `armada doctor` runtime detection, as noted above.

Guides: [INSTRUCTIONS_FOR_OPENCODE.md](INSTRUCTIONS_FOR_OPENCODE.md),
[OPENCODE_AS_ORCHESTRATOR.md](OPENCODE_AS_ORCHESTRATOR.md).

### ApiEndpoint

No process is started. `ApiAgentRuntime` resolves the captain's model endpoint (configured under Inference endpoints in
the dashboard) and runs a tool-calling loop inside the Admiral, up to 100 iterations per run, with Armada's built-in
coding tools: read, write, edit, multi-edit, glob, grep, list and manage directories, file metadata, delete, run
process, and a task-plan pair. The model comes from the endpoint; a captain-level model overrides it. When Armada
supplies `ARMADA_MCP_URL` and `ARMADA_MCP_TOKEN`, the loop also connects to Armada's own MCP server as that caller and
exposes its tools to the model. Ask turns always supply them; missions supply them while `Mcp.MissionScopedTokens` is
on (the default), and otherwise a mission run by an API-endpoint captain has the coding tools only.

## Running agents safely

By default every CLI runtime is launched with its "do not ask me" switch on: `--dangerously-skip-permissions` for
Claude Code, `--sandbox workspace-write` for Codex (and `--dangerously-bypass-approvals-and-sandbox` on Windows),
`--approval-mode yolo` for Gemini, `--force` for Cursor, `--yolo` for Mux unless an approval policy is set, and `--auto`
for OpenCode. A captain runs unattended inside a git worktree, so there is nobody to answer a permission prompt; without
these flags a mission stalls or is refused on its first shell command.

The consequence is that a captain can run any command the Admiral's (or Harbor's) user account can run, on that
machine, with that user's credentials. The worktree is a working directory, not a sandbox. Run captains under an
account that has only what the work needs, keep secrets you do not want an agent to read out of that account's home
directory, and prefer a dedicated machine or VM for fleets that touch untrusted repositories.

Three settings turn the switch off:

- **Per captain:** `autoApprove` (default `true`) on `create_captain` / `update_captain`, stored in the captain's
  `runtimeOptionsJson`; in the dashboard it is the "Auto-approve agent tool use" checkbox on the captain form, and the
  TUI captain form has the same field. With it off, Claude Code runs with `--permission-mode acceptEdits --allowedTools
  mcp__armada`, Codex with `--sandbox workspace-write`, Gemini with `--approval-mode auto_edit`, Cursor without
  `--force`, OpenCode without `--auto`, and Mux with approval policy `deny` (unless `muxApprovalPolicy` is set). It does
  not apply to ApiEndpoint captains, which run Armada's coding tools in-process.
- **Per vessel:** `autoApprove` on `add_vessel` / `update_vessel` (`clearAutoApprove` removes it), shown as "Agent
  Auto-Approve" on the vessel form (On, Off, or use the captain setting). When set it wins over the captain's setting
  for missions on that vessel, including Harbor launches.
- **Ask Armada:** `Ask.CaptainAutoApprove` in `settings.json` (default `false`). While it is false, Ask thread turns and
  milestone narrations run CLI captains without auto-approve whatever the captain's own setting, because any
  authenticated user can start an Ask turn. Set it to `true` only when everyone who can use Ask is trusted with a shell
  on the Admiral host.

Every command Armada itself runs on a user's behalf (workspace exec, fleet action commands, check runs, Harbor probes,
merge-queue tests) is recorded as an `audit.command` event. See [SECURITY_REVIEW.md](SECURITY_REVIEW.md#running-agents-safely) for the threat
model.

Ask threads are the one place where a human approves Armada actions, and that gate covers Armada's own MCP tools, not
the agent's shell or file tools. Those are governed by the auto-approve settings above.
