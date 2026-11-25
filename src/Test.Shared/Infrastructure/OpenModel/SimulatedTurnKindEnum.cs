namespace Test.Shared.Infrastructure.OpenModel
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What one scripted reply from <see cref="OpenModelSimulator"/> looks like. Each value reproduces a behavior seen
    /// from open models or the servers that host them (Ollama, vLLM, LM Studio, proxies such as Conductor).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SimulatedTurnKindEnum
    {
        /// <summary>A plain assistant text reply that ends the turn.</summary>
        Text,

        /// <summary>A well-formed native tool call in the protocol's tool-call field.</summary>
        NativeToolCall,

        /// <summary>The tool call written as Hermes or Qwen text: <c>&lt;tool_call&gt;{json}&lt;/tool_call&gt;</c>.</summary>
        HermesTextToolCall,

        /// <summary>The tool call written as <c>&lt;function=name&gt;{json}&lt;/function&gt;</c> text.</summary>
        FunctionTagTextToolCall,

        /// <summary>The tool call written as a fenced JSON block with <c>name</c> and <c>arguments</c>.</summary>
        FencedJsonTextToolCall,

        /// <summary>A native tool call whose arguments are not valid JSON.</summary>
        MalformedToolArguments,

        /// <summary>A native tool call naming a tool that was not offered.</summary>
        UnknownToolName,

        /// <summary>Ollama's HTTP 500 when the model's tool-call output cannot be parsed.</summary>
        OllamaToolCallParse500,

        /// <summary>HTTP 429 without quota information: queue backpressure from a local server or proxy.</summary>
        Backpressure429,

        /// <summary>HTTP 429 with an <c>insufficient_quota</c> error: a real usage limit.</summary>
        QuotaExhausted429,

        /// <summary>Ollama's HTTP 500 after the model runner process died.</summary>
        RunnerTerminated500,

        /// <summary>Ollama's HTTP 500 when the model does not fit in memory.</summary>
        OutOfMemory500,

        /// <summary>HTTP 400 because the prompt is longer than the served context.</summary>
        ContextOverflow400,

        /// <summary>A reply cut off by the token limit (<c>length</c>).</summary>
        LengthCut,

        /// <summary>Reasoning text that ends with <c>&lt;/think&gt;</c> but never opened the tag, followed by the answer.</summary>
        ReasoningWithoutOpenTag,

        /// <summary>No response headers until the client gives up (a cold load or a stuck runner).</summary>
        Hang,

        /// <summary>An arbitrary status code and body taken from <see cref="SimulatedTurn.Body"/>.</summary>
        RawResponse
    }
}
