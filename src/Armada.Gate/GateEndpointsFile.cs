namespace Armada.Gate
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// benchmarks/config/gate-endpoints.local.json (git-ignored): where the open models are served and the credentials
    /// for them. Its values never appear in tracked files, reports, or logs.
    /// </summary>
    public sealed class GateEndpointsFile
    {
        /// <summary>Conductor, the preferred source.</summary>
        [JsonPropertyName("conductor")]
        public GateConductor? Conductor { get; set; }

        /// <summary>Ollama Cloud, the frugal fallback.</summary>
        [JsonPropertyName("ollamaCloud")]
        public GateOllamaCloud? OllamaCloud { get; set; }
    }
}
