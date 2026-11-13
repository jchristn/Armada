namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of Ollama's GET /api/ps (the models loaded now), read for the served-context probe.
    /// </summary>
    public class OllamaPsResponse
    {
        /// <summary>
        /// Loaded models, or null.
        /// </summary>
        [JsonPropertyName("models")]
        public List<OllamaPsModel>? Models { get; set; } = null;
    }
}
