namespace Armada.Runtimes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The JSON body of a tool call a model wrote as text: {"name": ..., "arguments": {...}}, with "parameters" accepted
    /// for the arguments and an OpenAI-style {"function": {...}} wrapper accepted around both.
    /// </summary>
    public sealed class TextToolCallPayload
    {
        /// <summary>Tool name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>Arguments object, or a string holding the arguments JSON.</summary>
        [JsonPropertyName("arguments")]
        public object? Arguments { get; set; } = null;

        /// <summary>Arguments under the alternative key some models use.</summary>
        [JsonPropertyName("parameters")]
        public object? Parameters { get; set; } = null;

        /// <summary>OpenAI-style wrapper holding the name and arguments.</summary>
        [JsonPropertyName("function")]
        public TextToolCallPayload? Function { get; set; } = null;
    }
}
