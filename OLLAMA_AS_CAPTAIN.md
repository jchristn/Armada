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
Armada.Agent.Ollama (C# CLI, .NET global tool)
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

The wrapper agent is the key piece. It turns Ollama from a text generator into an autonomous coding agent. Built in C# as a .NET global tool, it shares the same ecosystem as Armada itself.

## Implementation Steps

### Step 1: Create `OllamaRuntime.cs`

**File:** `src/Armada.Runtimes/OllamaRuntime.cs`

```csharp
public class OllamaRuntime : BaseAgentRuntime
{
    public override string Name => "Ollama";
    public override bool SupportsResume => false;

    private string _ExecutablePath = "armada-ollama"; // the .NET global tool
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

### Step 4: Build the Wrapper Agent (`Armada.Agent.Ollama`)

A new C# project in the Armada solution, packaged as a .NET global tool (`armada-ollama`).

**Project structure:**

```
src/Armada.Agent.Ollama/
    Armada.Agent.Ollama.csproj    (PackAsTool, ToolCommandName: armada-ollama)
    Program.cs                     (entry point, argument parsing)
    OllamaClient.cs                (HTTP client for Ollama API)
    AgentLoop.cs                   (tool-use loop orchestrator)
    Tools/
        ReadFileTool.cs
        WriteFileTool.cs
        EditFileTool.cs
        RunCommandTool.cs
        ListFilesTool.cs
        SearchFilesTool.cs
    Context/
        ClaudeMdReader.cs          (reads and parses CLAUDE.md)
        FileTreeBuilder.cs         (builds file tree for context)
        ContextManager.cs          (manages context window budget)
    Git/
        GitCommitter.cs            (stages, commits with Armada trailers)
```

**Core loop in `AgentLoop.cs`:**

```csharp
public async Task<int> RunAsync(string workingDirectory, string prompt, string model, string host)
{
    // 1. Read CLAUDE.md
    string claudeMd = ClaudeMdReader.Read(workingDirectory);

    // 2. Build initial context
    string fileTree = FileTreeBuilder.Build(workingDirectory);
    string systemPrompt = BuildSystemPrompt(claudeMd, fileTree);

    // 3. Initialize conversation
    List<ChatMessage> messages = new List<ChatMessage>();
    messages.Add(new ChatMessage("system", systemPrompt));
    messages.Add(new ChatMessage("user", prompt));

    // 4. Tool-use loop
    for (int iteration = 0; iteration < _MaxIterations; iteration++)
    {
        ChatResponse response = await _Client.ChatAsync(model, messages, _Tools);

        if (response.Done || !response.HasToolCalls)
        {
            Console.WriteLine(response.Content);
            break;
        }

        // Execute tool calls
        foreach (ToolCall call in response.ToolCalls)
        {
            string result = await ExecuteToolAsync(call, workingDirectory);
            messages.Add(new ChatMessage("tool", result, call.Id));
            Console.WriteLine("[ARMADA:PROGRESS] " + ((iteration + 1) * 100 / _MaxIterations));
        }
    }

    // 5. Commit changes
    await GitCommitter.CommitAsync(workingDirectory, prompt, _ArmadaTrailers);

    return 0; // success
}
```

**`OllamaClient.cs`** wraps the Ollama HTTP API:

```csharp
public class OllamaClient
{
    private HttpClient _Http;
    private string _BaseUrl;

    public async Task<ChatResponse> ChatAsync(
        string model,
        List<ChatMessage> messages,
        List<ToolDefinition> tools)
    {
        // POST /api/chat with model, messages, tools
        // Parse response including tool_calls array
    }
}
```

**Tool definitions** follow Ollama's tool calling format (JSON schema):

```csharp
public static List<ToolDefinition> GetTools()
{
    return new List<ToolDefinition>
    {
        new ToolDefinition("read_file", "Read a file from the working directory",
            new { type = "object", properties = new {
                path = new { type = "string", description = "File path relative to working directory" }
            }, required = new[] { "path" } }),

        new ToolDefinition("write_file", "Create or overwrite a file",
            new { type = "object", properties = new {
                path = new { type = "string", description = "File path" },
                content = new { type = "string", description = "File content" }
            }, required = new[] { "path", "content" } }),

        new ToolDefinition("edit_file", "Find and replace text in a file",
            new { type = "object", properties = new {
                path = new { type = "string", description = "File path" },
                old_text = new { type = "string", description = "Text to find" },
                new_text = new { type = "string", description = "Replacement text" }
            }, required = new[] { "path", "old_text", "new_text" } }),

        new ToolDefinition("run_command", "Execute a shell command",
            new { type = "object", properties = new {
                command = new { type = "string", description = "Command to execute" }
            }, required = new[] { "command" } }),

        new ToolDefinition("list_files", "List directory contents",
            new { type = "object", properties = new {
                directory = new { type = "string", description = "Directory path (default: .)" }
            } }),

        new ToolDefinition("search_files", "Search for files matching a pattern",
            new { type = "object", properties = new {
                pattern = new { type = "string", description = "Glob pattern" }
            }, required = new[] { "pattern" } }),
    };
}
```

### Step 5: Update Dashboard

Add "Ollama" to the runtime dropdown in Captain creation (dashboard + MCP tool schema).

## Model Requirements

Not all Ollama models are suitable for coding agents. Recommended:

| Model | Size | Coding Quality | Tool Calling | Speed |
|-------|------|---------------|-------------|-------|
| `qwen2.5-coder:32b` | 32B | Very good | Yes | Slow |
| `deepseek-coder:33b` | 33B | Very good | Limited | Slow |
| `codellama:34b` | 34B | Good | No | Slow |
| `codellama:13b` | 13B | Decent | No | Moderate |
| `llama3.1:70b` | 70B | Good (general) | Yes | Very slow |

Smaller models (7B) are generally too weak for autonomous coding tasks. Tool calling support varies by model -- without it, the agent loop must parse natural language for tool invocations, which is fragile.

## Captain Settings for Model Parameters

Model-related settings should be stored on the Captain, not on the runtime class. This allows different captains to use different models and parameters even with the same runtime type.

**New fields on the Captain model:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `ModelName` | string? | null | Model name (e.g. `codellama:13b`, `qwen2.5-coder:32b`) |
| `ModelHost` | string? | null | Ollama server URL (e.g. `http://localhost:11434`) |
| `Temperature` | double? | null | Generation temperature (0.0-2.0) |
| `MaxTokens` | int? | null | Max tokens per generation |
| `MaxIterations` | int? | null | Max tool-use loop iterations |
| `ContextBudget` | int? | null | Max context tokens to send |

When these are null, the runtime uses its built-in defaults. When set, they override.

**How to pass to the wrapper:** The runtime reads these from the captain record and passes them as CLI arguments:

```csharp
protected override List<string> BuildArguments(string prompt)
{
    List<string> args = new List<string>();
    args.Add("--model"); args.Add(_Model);
    args.Add("--host"); args.Add(_OllamaHost);
    if (_Temperature.HasValue) { args.Add("--temperature"); args.Add(_Temperature.Value.ToString()); }
    if (_MaxTokens.HasValue) { args.Add("--max-tokens"); args.Add(_MaxTokens.Value.ToString()); }
    if (_MaxIterations.HasValue) { args.Add("--max-iterations"); args.Add(_MaxIterations.Value.ToString()); }
    args.Add("--prompt"); args.Add(prompt);
    return args;
}
```

**Database migration:** Add nullable columns to the `captains` table:
```sql
ALTER TABLE captains ADD COLUMN model_name TEXT;
ALTER TABLE captains ADD COLUMN model_host TEXT;
ALTER TABLE captains ADD COLUMN temperature REAL;
ALTER TABLE captains ADD COLUMN max_tokens INTEGER;
ALTER TABLE captains ADD COLUMN max_iterations INTEGER;
ALTER TABLE captains ADD COLUMN context_budget INTEGER;
```

**Dashboard:** Add these fields to the Captain create/edit form, visible when the runtime is Ollama or vLLM.

## Shared Agent Library

Since the Ollama and vLLM wrappers share 90% of their code (tools, context, git, agent loop), extract the shared components into a common library:

```
src/Armada.Agent.Common/
    Armada.Agent.Common.csproj
    AgentLoop.cs                    (abstract, backend-agnostic)
    ILlmClient.cs                   (interface for chat API)
    Tools/
        ITool.cs
        ReadFileTool.cs
        WriteFileTool.cs
        EditFileTool.cs
        RunCommandTool.cs
        ListFilesTool.cs
        SearchFilesTool.cs
    Context/
        ClaudeMdReader.cs
        FileTreeBuilder.cs
        ContextManager.cs
    Git/
        GitCommitter.cs
    Models/
        ChatMessage.cs
        ChatResponse.cs
        ToolCall.cs
        ToolDefinition.cs

src/Armada.Agent.Ollama/
    Armada.Agent.Ollama.csproj      (references Armada.Agent.Common)
    Program.cs
    OllamaClient.cs                 (implements ILlmClient)

src/Armada.Agent.Vllm/
    Armada.Agent.Vllm.csproj        (references Armada.Agent.Common)
    Program.cs
    VllmClient.cs                   (implements ILlmClient)
```

## Configuration

Default settings (overridden by captain-level settings when set):

| Setting | Default | Description |
|---------|---------|-------------|
| `OllamaHost` | `http://localhost:11434` | Ollama server URL |
| `Model` | `codellama:13b` | Model name |
| `MaxIterations` | 20 | Maximum tool-use loop iterations |
| `Temperature` | 0.1 | Generation temperature (low for coding) |
| `ContextWindow` | 8192 | Max context tokens |
| `GpuLayers` | -1 | Number of layers to offload to GPU (-1 = all) |

## Challenges and Limitations

1. **No native tool use in most models:** Unlike Claude or GPT-4, most open models don't natively support tool calling. The wrapper must handle this via prompt engineering or structured output parsing. Models with Hermes-format tool calling (Qwen, Llama 3.1) work best.

2. **Context window limits:** Most open models have 4K-8K context windows (vs 200K for Claude). The `ContextManager` must be aggressive -- only include relevant files, not the entire codebase. Budget tokens carefully between system prompt, file context, and conversation history.

3. **Code quality:** Open models produce lower quality code than frontier models. Expect more iterations, more failures, and simpler implementations.

4. **No CLAUDE.md awareness:** The model doesn't know what CLAUDE.md is. The `ClaudeMdReader` must parse it and inject the instructions into the system prompt.

5. **Speed:** Local inference on consumer hardware is 10-100x slower than API calls to Claude/GPT-4. Expect missions to take significantly longer.

6. **Multi-file edits:** Open models struggle with coordinated changes across multiple files. The agent loop may need to break complex edits into single-file steps with intermediate commits.

## Effort Estimate

| Component | Effort |
|-----------|--------|
| `OllamaRuntime.cs` + enum + factory | 1 hour |
| `Armada.Agent.Ollama` project scaffold + arg parsing | 2-3 hours |
| `OllamaClient.cs` (HTTP client) | 4-6 hours |
| Tool implementations (read, write, edit, command, list, search) | 1-2 days |
| `AgentLoop.cs` (tool-use orchestration) | 2-3 days |
| `ContextManager.cs` (token budgeting) | 1-2 days |
| `ClaudeMdReader.cs` + `FileTreeBuilder.cs` | 4-6 hours |
| `GitCommitter.cs` (commit with Armada trailers) | 2-3 hours |
| Testing across models | 1-2 weeks |
| Dashboard integration | 1 hour |

The wrapper agent is 95% of the work. The Armada integration is trivial.
