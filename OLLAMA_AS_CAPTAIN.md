# Ollama as an Armada Captain

This document describes what needs to be done to use [Ollama](https://ollama.ai/) as a captain runtime in Armada.

## The Challenge

Armada's runtime model assumes the agent is a CLI tool that:
1. Accepts a prompt as a command-line argument
2. Reads a `CLAUDE.md` file from the working directory for instructions
3. Has access to the filesystem (read/write files, run commands)
4. Writes output to stdout
5. Exits with code 0 on success

Ollama is a local LLM inference server. It runs models like Llama, Mistral, CodeLlama, etc. It exposes an HTTP API (`POST /api/generate`, `POST /api/chat`) but does NOT have a CLI that operates like Claude Code or Codex. Ollama generates text -- it does not autonomously read files, write code, or execute commands.

This means Ollama cannot be a drop-in replacement for Claude Code. A **wrapper agent** is needed that bridges Ollama's text generation with Armada's agentic expectations.

## Architecture: Wrapper Agent

```
Armada Admiral
    |
    v
OllamaRuntime (C# class, extends BaseAgentRuntime)
    |
    v
armada-ollama-agent (Python/Node CLI wrapper)
    |
    +--> Reads CLAUDE.md from working directory
    +--> Reads file tree for context
    +--> Calls Ollama HTTP API with prompt + context
    +--> Parses response for tool calls (file edits, shell commands)
    +--> Executes tool calls against the filesystem
    +--> Loops until the model says "done" or max iterations reached
    +--> Commits changes to git
    +--> Writes output to stdout
    +--> Exits with code 0
```

The wrapper agent is the key piece. It turns Ollama from a text generator into an autonomous coding agent.

## Implementation Steps

### Step 1: Create `OllamaRuntime.cs`

**File:** `src/Armada.Runtimes/OllamaRuntime.cs`

```csharp
public class OllamaRuntime : BaseAgentRuntime
{
    public override string Name => "Ollama";
    public override bool SupportsResume => false;

    private string _ExecutablePath = "armada-ollama-agent"; // the wrapper CLI
    private string _Model = "codellama:13b";
    private string _OllamaHost = "http://localhost:11434";

    protected override string GetCommand() => _ExecutablePath;

    protected override List<string> BuildArguments(string prompt)
    {
        return new List<string>
        {
            "--model", _Model,
            "--host", _OllamaHost,
            "--prompt", prompt
        };
    }

    protected override void ApplyEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["OLLAMA_HOST"] = _OllamaHost;
    }
}
```

### Step 2: Add `Ollama` to `AgentRuntimeEnum`

**File:** `src/Armada.Core/Enums/AgentRuntimeEnum.cs`

```csharp
[EnumMember(Value = "Ollama")]
Ollama,
```

### Step 3: Register in `AgentRuntimeFactory`

**File:** `src/Armada.Runtimes/AgentRuntimeFactory.cs`

```csharp
case AgentRuntimeEnum.Ollama:
    return new OllamaRuntime(_Logging);
```

### Step 4: Build the Wrapper Agent (`armada-ollama-agent`)

This is the hard part. The wrapper agent must:

1. **Parse arguments:** `--model`, `--host`, `--prompt`
2. **Read CLAUDE.md** from the current working directory for mission instructions
3. **Build context:** read the file tree, read relevant files mentioned in the prompt
4. **Construct system prompt:** combine CLAUDE.md instructions with file context
5. **Call Ollama API** in a loop:
   - Send the prompt + context to `POST /api/chat`
   - Parse the response for intended actions (file creates, edits, shell commands)
   - Execute those actions
   - Feed results back to the model
   - Repeat until the model indicates completion
6. **Handle tool use:** The model needs to output structured tool calls. Options:
   - Use a structured output format (JSON tool calls)
   - Parse natural language instructions ("Create file X with content Y")
   - Use a framework like LangChain or similar
7. **Commit changes:** run `git add -A && git commit -m "..."` with Armada trailers
8. **Report progress:** print `[ARMADA:PROGRESS] N` and `[ARMADA:STATUS] ...` to stdout
9. **Exit cleanly** with code 0

**Recommended language:** Python (best Ollama client library support)

**Recommended approach:** Use Ollama's tool/function calling support (available in newer models) to define tools for:
- `read_file(path)` -- read a file from the working directory
- `write_file(path, content)` -- create or overwrite a file
- `edit_file(path, old_text, new_text)` -- find and replace in a file
- `run_command(command)` -- execute a shell command
- `list_files(directory)` -- list directory contents
- `search_files(pattern)` -- search for files matching a glob

### Step 5: Update Dashboard

Add "Ollama" to the runtime dropdown in Captain creation (dashboard + MCP tool schema).

## Model Requirements

Not all Ollama models are suitable for coding agents. Recommended:

| Model | Size | Coding Quality | Speed |
|-------|------|---------------|-------|
| `codellama:34b` | 34B | Good | Slow |
| `codellama:13b` | 13B | Decent | Moderate |
| `deepseek-coder:33b` | 33B | Very good | Slow |
| `qwen2.5-coder:32b` | 32B | Very good | Slow |
| `llama3.1:70b` | 70B | Good (general) | Very slow |

Smaller models (7B) are generally too weak for autonomous coding tasks.

## Configuration

Settings that should be configurable per-captain or globally:

| Setting | Default | Description |
|---------|---------|-------------|
| `OllamaHost` | `http://localhost:11434` | Ollama server URL |
| `Model` | `codellama:13b` | Model name |
| `MaxIterations` | 20 | Maximum tool-use loop iterations |
| `Temperature` | 0.1 | Generation temperature (low for coding) |
| `ContextWindow` | 8192 | Max context tokens |
| `GpuLayers` | -1 | Number of layers to offload to GPU (-1 = all) |

## Challenges and Limitations

1. **No native tool use in most models:** Unlike Claude or GPT-4, most open models don't natively support tool calling. The wrapper must handle this via prompt engineering or structured output parsing.

2. **Context window limits:** Most open models have 4K-8K context windows (vs 200K for Claude). The wrapper must be aggressive about context management -- only include relevant files, not the entire codebase.

3. **Code quality:** Open models produce lower quality code than frontier models. Expect more iterations, more failures, and simpler implementations.

4. **No CLAUDE.md awareness:** The model doesn't know what CLAUDE.md is. The wrapper must read it and inject the instructions into the system prompt.

5. **Speed:** Local inference on consumer hardware is 10-100x slower than API calls to Claude/GPT-4. Expect missions to take significantly longer.

6. **Multi-file edits:** Open models struggle with coordinated changes across multiple files. The wrapper may need to break complex edits into single-file steps.

## Effort Estimate

| Component | Effort |
|-----------|--------|
| `OllamaRuntime.cs` + enum + factory | 1 hour |
| Wrapper agent (basic, single-file edits) | 2-3 days |
| Wrapper agent (full tool-use loop) | 1-2 weeks |
| Testing and iteration | 1-2 weeks |
| Dashboard integration | 1 hour |

The wrapper agent is 95% of the work. The Armada integration is trivial.
