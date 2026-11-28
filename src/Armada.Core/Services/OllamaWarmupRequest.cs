namespace Armada.Core.Services
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of an Ollama <c>POST /api/generate</c> load request: a model and a <c>keep_alive</c> with no prompt, which
    /// loads the model into memory without generating anything.
    /// </summary>
    public sealed class OllamaWarmupRequest
    {
        /// <summary>Model to load.</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = String.Empty;

        /// <summary>How long Ollama keeps the model loaded after the request, as an Ollama duration such as <c>30m</c>.</summary>
        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; set; } = OllamaModelWarmup.DefaultKeepAlive;
    }
}
