namespace Armada.Gate
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where Conductor serves the open models, from the git-ignored local endpoint file.
    /// </summary>
    public sealed class GateConductor
    {
        /// <summary>Base URL ending in /v1.0/api/.</summary>
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>Bearer token.</summary>
        [JsonPropertyName("bearerToken")]
        public string? BearerToken { get; set; }

        /// <summary>Models by key.</summary>
        [JsonPropertyName("models")]
        public Dictionary<string, GateConductorModel> Models { get; set; } = new Dictionary<string, GateConductorModel>(StringComparer.Ordinal);
    }
}
