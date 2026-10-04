# Captain Support Matrix

A captain is whatever actually does the work on a mission: a coding-agent CLI that Armada launches as a child
process, or an in-process loop that talks to an inference endpoint directly. Armada ships seven runtimes. They are not
equal. Claude Code gets the most attention in the code (token streaming, tool cards, gated Ask threads), Mux and
OpenCode come close because they speak structured JSON, and Codex, Gemini, and Cursor are driven as plain-text CLIs.

Everything below is read from the runtime code (`src/Armada.Runtimes`, `src/Armada.Core/Services`, and the chat and
planning coordinators in `src/Armada.Server`), not from vendor documentation. When a vendor CLI changes a flag, the
code is the thing to check first.

## Supported runtimes

| Runtime | `AgentRuntimeEnum` | Executable Armada runs | Install hint printed by `armada doctor` |
|---------|--------------------|------------------------|------------------------------------------|
| Claude Code | `ClaudeCode` | `claude` | `npm install -g @anthropic-ai/claude-code` |
| Codex | `Codex` | `codex` | `npm install -g @openai/codex` |
| Gemini | `Gemini` | `gemini` | `npm install -g @google/gemini-cli` |
| Cursor | `Cursor` | `cursor-agent` | See https://docs.cursor.com/cli |
| Mux | `Mux` | `mux` | Install mux and put `mux` on PATH |
| OpenCode | `OpenCode` | `opencode` | (none; not probed by `armada doctor`) |
| API endpoint | `ApiEndpoint` | none (runs in the Admiral process) | Configure an inference endpoint in the dashboard |

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
| Ask approval gating | Yes | No | No | No | No | No | Yes |
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
`CaptainChatService` binds that token for exactly two runtimes. ApiEndpoint captains get it through `ARMADA_MCP_URL` and
`ARMADA_MCP_TOKEN`. Claude Code gets a per-launch strict MCP config with the token in an `X-Token` header. Every other
CLI runtime keeps whatever MCP configuration the host user has, so its Armada tool calls arrive as ordinary
authenticated calls and are not held for approval. The dashboard reflects the same split: the Ask page treats only
`ClaudeCode` and `ApiEndpoint` as runtimes the server connects to Armada's tools. W6.3 in `V1_READINESS.md` tracks
extending this.

**Planning** runs for any CLI runtime. ApiEndpoint and Harbor-hosted (remote) runtimes report
`SupportsPlanningSessions = false`, and the coordinator refuses them. The user-facing reason string on `Captain` still
lists only Claude Code, Codex, Gemini, Cursor, and Mux; OpenCode works anyway because it inherits the base CLI runtime.

**Streaming.** Claude Code switches to `--output-format stream-json --include-partial-messages` for Ask and interactive
planning turns, which gives real token deltas plus a clean final message and token counts from the `result` event.
Missions keep plain `--print` output. Mux streams `assistant_text` JSONL events, and OpenCode streams `--format json`
events. Codex, Gemini, and Cursor are read one stdout line at a time. The ApiEndpoint loop emits each model response
whole, between tool calls.

**Thinking.** Only Mux (`--show-thinking`, `assistant_thinking` events) and OpenCode (`--thinking`, `reasoning`
events) have a native thinking channel. For every other runtime, when the user asks to see thinking, Armada appends an
instruction to wrap reasoning in a single `<thinking>...</thinking>` block and lifts that block out of the final reply.
That is the model describing its reasoning on request, not the provider's reasoning trace.

**Tool cards** come from parsing structured output: Claude Code `tool_use` and `tool_result` blocks, Mux
`tool_call_proposed` and `tool_call_completed` events, OpenCode `tool_use` parts, and ApiEndpoint
`[ARMADA:TOOLEVENT]` lines. Plain-text runtimes produce no cards.

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
MCP server at `http://localhost:<mcpPort>/mcp`, and points the CLI at it. Each runtime section names the mechanism.

### Claude Code

```
claude --print --verbose [--output-format stream-json --include-partial-messages] [--model <model>] --dangerously-skip-permissions
```

The stream-json flags are added only for Ask and interactive planning turns. Armada sets
`CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT=1`, removes `CLAUDECODE` and `CLAUDE_CODE_ENTRYPOINT` so a captain can start
even when the Admiral itself was launched from inside a Claude Code session, and sets `MAX_THINKING_TOKENS` from the
captain's reasoning effort. Isolated launches add `--setting-sources project,local --strict-mcp-config --mcp-config
<dir>/armada-mcp.json`. Ask thread turns always use that isolated form, with the session token as an `X-Token` header,
which is what makes approval gating work. Claude Code is the only runtime that reports `SupportsResume`.

Guides: [INSTRUCTIONS_FOR_CLAUDE_CODE.md](INSTRUCTIONS_FOR_CLAUDE_CODE.md),
[CLAUDE_CODE_AS_ORCHESTRATOR.md](CLAUDE_CODE_AS_ORCHESTRATOR.md).

### Codex

```
codex exec --full-auto [--model <model>] [-c model_reasoning_effort=<level>] [--output-last-message <file>]
```

The approval mode defaults to `full-auto`. On Windows, `full-auto` is sent as `--dangerously-bypass-approvals-and-sandbox`
instead, and a `dangerous` mode sends that flag on every OS. The final reply is read from the
`--output-last-message` file. Isolated launches write `config.toml` with an `[mcp_servers.armada]` entry and set
`CODEX_HOME` to the scoped directory. When Armada validates a Codex captain it first runs `git init` in the scratch
directory, since Codex expects to run inside a git repository.

Guides: [INSTRUCTIONS_FOR_CODEX.md](INSTRUCTIONS_FOR_CODEX.md), [CODEX_AS_ORCHESTRATOR.md](CODEX_AS_ORCHESTRATOR.md).

### Gemini

```
gemini [--model <model>] --approval-mode yolo
```

Gemini reads the prompt from piped stdin when no `-p` is given. Isolated launches write `.gemini/settings.json` with
an `mcpServers.armada` entry and point `HOME` and `USERPROFILE` at the scoped directory.

Guides: [INSTRUCTIONS_FOR_GEMINI.md](INSTRUCTIONS_FOR_GEMINI.md), [GEMINI_AS_ORCHESTRATOR.md](GEMINI_AS_ORCHESTRATOR.md).

### Cursor

```
cursor-agent -p [--model <model>] --force --output-format text
```

Isolated launches write `.cursor/mcp.json` and override `HOME` and `USERPROFILE` the same way as Gemini. Outside
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
`MUX_CONFIG_DIR`, then `~/.mux`). An empty or `auto` approval policy becomes `--yolo`; anything else is passed as
`--approval-policy`. Mux is also the only runtime where the approval policy can be changed per captain
(`muxApprovalPolicy` on the captain MCP tools). Isolated launches write `mcp-servers.json` and set `MUX_CONFIG_DIR`.
Mux is the one CLI runtime that takes the prompt as a positional argument (the last one) rather than on stdin.

Guides: [INSTRUCTIONS_FOR_MUX.md](INSTRUCTIONS_FOR_MUX.md), [MUX_AS_ORCHESTRATOR.md](MUX_AS_ORCHESTRATOR.md).

### OpenCode

```
opencode run --format json [--model <model>] [--variant <variant>] [--thinking] --auto --dir <workingDirectory>
```

OpenCode has no entry in the launch isolation planner, so `IsolateCaptainLaunch` has no effect on it; it always reads
the user's `~/.config/opencode/opencode.json` (or `.jsonc`), where `armada mcp install` writes a `remote` entry under
`mcp`. It is also missing from `armada doctor` runtime detection, as noted above.

Guides: [INSTRUCTIONS_FOR_OPENCODE.md](INSTRUCTIONS_FOR_OPENCODE.md),
[OPENCODE_AS_ORCHESTRATOR.md](OPENCODE_AS_ORCHESTRATOR.md).

### ApiEndpoint

No process is started. `ApiAgentRuntime` resolves the captain's model endpoint (configured under Inference endpoints in
the dashboard) and runs a tool-calling loop inside the Admiral, up to 100 iterations per run, with Armada's built-in
coding tools: read, write, edit, multi-edit, glob, grep, list and manage directories, file metadata, delete, run
process, and a task-plan pair. The model comes from the endpoint; a captain-level model overrides it. When Armada
supplies `ARMADA_MCP_URL` and `ARMADA_MCP_TOKEN` (Ask turns do), the loop also connects to Armada's own MCP server as
that user and exposes its tools to the model. Missions do not supply them, so a mission run by an API-endpoint captain
has the coding tools only.

## Running agents safely

Every CLI runtime is launched with its "do not ask me" switch on by default: `--dangerously-skip-permissions` for
Claude Code, `--full-auto` for Codex (and `--dangerously-bypass-approvals-and-sandbox` on Windows), `--approval-mode
yolo` for Gemini, `--force` for Cursor, `--yolo` for Mux unless an approval policy is set, and `--auto` for OpenCode.
A captain runs unattended inside a git worktree, so there is nobody to answer a permission prompt; without these flags
the mission would stall on its first shell command.

The consequence is that a captain can run any command the Admiral's (or Harbor's) user account can run, on that
machine, with that user's credentials. The worktree is a working directory, not a sandbox. Run captains under an
account that has only what the work needs, keep secrets you do not want an agent to read out of that account's home
directory, and prefer a dedicated machine or VM for fleets that touch untrusted repositories. Only Mux's approval mode
can be changed per captain today. Making the others configurable, and auditing every command Armada runs on a user's
behalf, is W1.5 in `V1_READINESS.md`.

Ask threads are the one place where a human sits in the loop, and the gate there covers Armada's own MCP tools, not
the agent's shell. Even on a gated Claude Code thread, the captain's built-in file and shell tools run without
prompting.
