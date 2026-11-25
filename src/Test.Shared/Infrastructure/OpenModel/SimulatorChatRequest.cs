namespace Test.Shared.Infrastructure.OpenModel
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The fields of an Ollama or OpenAI-compatible chat request that <see cref="OpenModelSimulator"/> reads.
    /// </summary>
    public sealed class SimulatorChatRequest
    {
        /// <summary>Requested model, or null.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Whether the client asked for streaming; null when the field was absent.</summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }
    }
}
