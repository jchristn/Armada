namespace Armada.Gate
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama Cloud access, from the git-ignored local endpoint file.
    /// </summary>
    public sealed class GateOllamaCloud
    {
        /// <summary>Base URL.</summary>
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://ollama.com/";

        /// <summary>API key.</summary>
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        /// <summary>Model names by key.</summary>
        [JsonPropertyName("models")]
        public Dictionary<string, string> Models { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
