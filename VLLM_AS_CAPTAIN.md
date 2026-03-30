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
armada-vllm-agent (Python CLI wrapper)
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
- You can use the `openai` Python library directly (just point `base_url` to vLLM)
- Tool/function calling works if the model supports it (e.g., Hermes, Qwen)
- Structured output via JSON mode is available
- The wrapper agent can be simpler because the API is more standardized

## Implementation Steps

### Step 1: Create `VllmRuntime.cs`

**File:** `src/Armada.Runtimes/VllmRuntime.cs`

```csharp
public class VllmRuntime : BaseAgentRuntime
{
    public override string Name => "vLLM";
    public override bool SupportsResume => false;

    private string _ExecutablePath = "armada-vllm-agent"; // the wrapper CLI
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

### Step 4: Build the Wrapper Agent (`armada-vllm-agent`)

The wrapper agent is nearly identical to the Ollama wrapper (see `OLLAMA_AS_CAPTAIN.md`) but uses the OpenAI client library instead of the Ollama API:

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8000/v1",
    api_key="not-needed"  # vLLM doesn't require auth by default
)

response = client.chat.completions.create(
    model="Qwen/Qwen2.5-Coder-32B-Instruct",
    messages=[
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt}
    ],
    tools=tools,  # function calling definitions
    temperature=0.1
)
```

The wrapper must implement the same tool-use loop:
1. Read CLAUDE.md and build context
2. Call vLLM with tools defined (read_file, write_file, edit_file, run_command, etc.)
3. Execute tool calls
4. Feed results back
5. Loop until done
6. Commit and exit

### Step 5: vLLM Server Setup

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

### Step 6: Update Dashboard

Add "vLLM" to the runtime dropdown in Captain creation.

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

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `BaseUrl` | `http://localhost:8000/v1` | vLLM server URL |
| `Model` | `Qwen/Qwen2.5-Coder-32B-Instruct` | Model name (must match what vLLM loaded) |
| `MaxIterations` | 20 | Maximum tool-use loop iterations |
| `Temperature` | 0.1 | Generation temperature |
| `MaxTokens` | 4096 | Max tokens per generation |
| `ToolCallParser` | `hermes` | Tool call format parser |

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

## Shared Wrapper Agent

The Ollama and vLLM wrapper agents are nearly identical -- the only difference is the API client. Consider building a single `armada-local-agent` that accepts a `--backend` flag:

```bash
armada-local-agent --backend ollama --model codellama:13b --prompt "..."
armada-local-agent --backend vllm --model Qwen/Qwen2.5-Coder-32B-Instruct --prompt "..."
```

This avoids maintaining two separate wrapper implementations.

## Challenges and Limitations

1. **vLLM server must be pre-started:** Armada cannot start/stop vLLM on demand. The user manages the server lifecycle independently.

2. **GPU memory:** Large coding models (32B+) require 40-80GB VRAM. Consumer GPUs (24GB) can only run smaller models or quantized versions.

3. **Model quality gap:** Even the best open models are significantly behind Claude Opus/Sonnet for autonomous coding. Expect:
   - More failed missions
   - Simpler implementations
   - Worse multi-file coordination
   - More iterations needed

4. **Quantization tradeoffs:** Running quantized models (GPTQ, AWQ, GGUF) saves VRAM but reduces quality. For coding tasks, quality matters more than speed.

5. **Context window:** Most open models have smaller context windows than frontier APIs. The wrapper must be aggressive about context management.

## Effort Estimate

| Component | Effort |
|-----------|--------|
| `VllmRuntime.cs` + enum + factory | 1 hour |
| Shared wrapper agent (`armada-local-agent`) | 2-3 days |
| Full tool-use loop with error handling | 1-2 weeks |
| Testing across multiple models | 1-2 weeks |
| Dashboard integration | 1 hour |
| Documentation | 2-3 hours |

If building the shared wrapper agent alongside the Ollama integration, the vLLM-specific work is minimal (swap the API client).
