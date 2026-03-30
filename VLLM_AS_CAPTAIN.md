# vLLM as an Armada Captain

This document describes what needs to be done to use [vLLM](https://docs.vllm.ai/) as a captain runtime in Armada.

## The Challenge

Same fundamental challenge as Ollama: vLLM is an inference engine, not an autonomous coding agent. It serves models via an OpenAI-compatible API endpoint but does not read files, write code, or execute commands on its own.

vLLM differs from Ollama in important ways:
- **Server-only:** vLLM runs as a persistent server process (`python -m vllm.entrypoints.openai.api_server`). There is no CLI that accepts a prompt and exits.
- **OpenAI-compatible API:** Exposes `POST /v1/completions` and `POST /v1/chat/completions` endpoints that match the OpenAI API format.
- **GPU-optimized:** Designed for high-throughput production inference with PagedAttention, continuous batching, and tensor parallelism.
- **Model loading:** Models are loaded from HuggingFace Hub or local paths on server startup, not per-request.

Like Ollama, a **wrapper agent** is required.

## Architecture: Wrapper Agent

```
Armada Admiral
    |
    v
VllmRuntime (C# class, extends BaseAgentRuntime)
    |
    v
Armada.Agent.Vllm (C# CLI, .NET global tool)
    |
    +--> Reads CLAUDE.md from working directory
    +--> Reads file tree for context
    +--> Calls vLLM OpenAI-compatible API with prompt + context
    +--> Parses response for tool calls (file edits, shell commands)
    +--> Executes tool calls against the filesystem
    +--> Loops until the model says "done" or max iterations reached
    +--> Commits changes to git
    +--> Writes output to stdout
    +--> Exits with code 0
```

## Key Difference from Ollama

vLLM uses the OpenAI API format, which means:
- The C# wrapper can use any OpenAI-compatible HTTP client (the API is well-documented JSON)
- Tool/function calling works if the model supports it (e.g., Hermes, Qwen)
- Structured output via JSON mode is available
- The `VllmClient.cs` can share most code with `OllamaClient.cs` since both are HTTP+JSON

## Implementation Steps

### Step 1: Create `VllmRuntime.cs`

**File:** `src/Armada.Runtimes/VllmRuntime.cs`

```csharp
public class VllmRuntime : BaseAgentRuntime
{
    public override string Name => "vLLM";
    public override bool SupportsResume => false;

    private string _ExecutablePath = "armada-vllm"; // the .NET global tool
    private string _Model = "Qwen/Qwen2.5-Coder-32B-Instruct";
    private string _BaseUrl = "http://localhost:8000/v1";

    protected override string GetCommand() => _ExecutablePath;

    protected override List<string> BuildArguments(string prompt)
    {
        return new List<string>
        {
            "--model", _Model,
            "--base-url", _BaseUrl,
            "--prompt", prompt
        };
    }

    protected override void ApplyEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["VLLM_BASE_URL"] = _BaseUrl;
    }
}
```

### Step 2: Add `vLLM` to `AgentRuntimeEnum`

**File:** `src/Armada.Core/Enums/AgentRuntimeEnum.cs`

```csharp
[EnumMember(Value = "vLLM")]
vLLM,
```

### Step 3: Register in `AgentRuntimeFactory`

**File:** `src/Armada.Runtimes/AgentRuntimeFactory.cs`

```csharp
case AgentRuntimeEnum.vLLM:
    return new VllmRuntime(_Logging);
```

### Step 4: Build the Wrapper Agent (`Armada.Agent.Vllm`)

A new C# project in the Armada solution, packaged as a .NET global tool (`armada-vllm`).

**Project structure:**

```
src/Armada.Agent.Vllm/
    Armada.Agent.Vllm.csproj       (PackAsTool, ToolCommandName: armada-vllm)
    Program.cs                      (entry point, argument parsing)
    VllmClient.cs                   (HTTP client for OpenAI-compatible API)
    AgentLoop.cs                    (tool-use loop orchestrator)
    Tools/                          (same as Ollama -- shared via Armada.Agent.Common)
        ReadFileTool.cs
        WriteFileTool.cs
        EditFileTool.cs
        RunCommandTool.cs
        ListFilesTool.cs
        SearchFilesTool.cs
    Context/                        (same as Ollama -- shared via Armada.Agent.Common)
        ClaudeMdReader.cs
        FileTreeBuilder.cs
        ContextManager.cs
    Git/
        GitCommitter.cs
```

**`VllmClient.cs`** wraps the OpenAI-compatible chat completions API:

```csharp
public class VllmClient
{
    private HttpClient _Http;
    private string _BaseUrl;

    public async Task<ChatResponse> ChatAsync(
        string model,
        List<ChatMessage> messages,
        List<ToolDefinition> tools,
        double temperature = 0.1,
        int maxTokens = 4096)
    {
        // POST /v1/chat/completions
        // Body:
        // {
        //   "model": "Qwen/Qwen2.5-Coder-32B-Instruct",
        //   "messages": [...],
        //   "tools": [...],
        //   "temperature": 0.1,
        //   "max_tokens": 4096
        // }
        //
        // Response follows OpenAI format:
        // { "choices": [{ "message": { "content": "...", "tool_calls": [...] } }] }
    }
}
```

The agent loop is identical to the Ollama wrapper -- only the HTTP client differs.

### Step 5: Shared Agent Library (`Armada.Agent.Common`)

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

### Step 6: vLLM Server Setup

Unlike Ollama (which manages its own server), vLLM must be started separately:

```bash
# Start vLLM server (must be running before dispatching missions)
python -m vllm.entrypoints.openai.api_server \
    --model Qwen/Qwen2.5-Coder-32B-Instruct \
    --host 0.0.0.0 \
    --port 8000 \
    --max-model-len 32768 \
    --gpu-memory-utilization 0.9 \
    --enable-auto-tool-choice \
    --tool-call-parser hermes
```

Armada does NOT manage the vLLM server lifecycle. The user is responsible for:
- Starting vLLM before dispatching missions
- Ensuring the model is loaded and ready
- Managing GPU memory and model selection

### Step 7: Update Dashboard

Add "vLLM" to the runtime dropdown in Captain creation.

## Captain Settings for Model Parameters

Model-related settings should be stored on the Captain, not on the runtime class. This allows different captains to use different models and parameters even with the same runtime type.

**New fields on the Captain model:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `ModelName` | string? | null | Model name/path (e.g. `Qwen/Qwen2.5-Coder-32B-Instruct`) |
| `ModelHost` | string? | null | Inference server URL (e.g. `http://localhost:8000/v1`) |
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
    args.Add("--base-url"); args.Add(_BaseUrl);
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

## Model Requirements

vLLM supports any HuggingFace-compatible model. For coding agents, recommended models:

| Model | Size | Tool Calling | Context | Notes |
|-------|------|-------------|---------|-------|
| `Qwen/Qwen2.5-Coder-32B-Instruct` | 32B | Yes (Hermes format) | 32K | Best open coding model |
| `deepseek-ai/DeepSeek-Coder-V2-Instruct` | 16B active (236B MoE) | Limited | 128K | Large context, MoE |
| `codellama/CodeLlama-34b-Instruct-hf` | 34B | No | 16K | Solid but no tool calling |
| `NousResearch/Hermes-3-Llama-3.1-70B` | 70B | Yes (native) | 8K | General + tools |
| `mistralai/Codestral-22B-v0.1` | 22B | Limited | 32K | Fast, decent quality |

Tool calling support is critical. Without it, the wrapper must parse natural language for actions, which is fragile.

## vLLM vs Ollama: Which to Choose

| Factor | Ollama | vLLM |
|--------|--------|------|
| **Setup** | Single binary, easy | Python environment, CUDA required |
| **API** | Custom (simpler) | OpenAI-compatible (more standard) |
| **Performance** | Good for single-user | Optimized for throughput (batching) |
| **Tool calling** | Limited model support | Better support via Hermes parser |
| **GPU memory** | Automatic management | Manual configuration |
| **Multi-GPU** | Limited | Tensor parallelism built-in |
| **Model support** | Curated library (ollama.com) | Any HuggingFace model |
| **Best for** | Local dev, single captain | Production, multiple captains |

**Choose Ollama** if you want simplicity and are running 1-2 captains on a single GPU.

**Choose vLLM** if you want production throughput, multiple concurrent captains, or multi-GPU inference.

## Challenges and Limitations

1. **vLLM server must be pre-started:** Armada cannot start/stop vLLM on demand. The user manages the server lifecycle independently.

2. **GPU memory:** Large coding models (32B+) require 40-80GB VRAM. Consumer GPUs (24GB) can only run smaller models or quantized versions.

3. **Model quality gap:** Even the best open models are significantly behind Claude Opus/Sonnet for autonomous coding. Expect more failed missions, simpler implementations, worse multi-file coordination, and more iterations needed.

4. **Quantization tradeoffs:** Running quantized models (GPTQ, AWQ, GGUF) saves VRAM but reduces quality. For coding tasks, quality matters more than speed.

5. **Context window:** Most open models have smaller context windows than frontier APIs. The `ContextManager` must be aggressive about context management.

## Effort Estimate

| Component | Effort |
|-----------|--------|
| `VllmRuntime.cs` + enum + factory | 1 hour |
| `Armada.Agent.Common` (shared library) | 2-3 days |
| `Armada.Agent.Vllm` project + `VllmClient.cs` | 1 day |
| Tool implementations (read, write, edit, command, list, search) | 1-2 days |
| `AgentLoop.cs` (tool-use orchestration) | 2-3 days |
| `ContextManager.cs` (token budgeting) | 1-2 days |
| Captain model settings (DB migration, MCP, REST, dashboard) | 1 day |
| Testing across models | 1-2 weeks |
| Dashboard integration | 1 hour |

If building the shared `Armada.Agent.Common` library alongside the Ollama integration, the vLLM-specific work is minimal (swap the API client -- `VllmClient` implements the same `ILlmClient` interface as `OllamaClient`).
