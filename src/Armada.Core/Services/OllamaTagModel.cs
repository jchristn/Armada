namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One installed model in Ollama's GET /api/tags.
    /// </summary>
    public class OllamaTagModel
    {
        /// <summary>
        /// Model name with its tag (for example "qwen3-coder:30b"), or null.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Model identifier (usually the same as <see cref="Name"/>), or null.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Content digest of the model (sha256 hex), or null.
        /// </summary>
        [JsonPropertyName("digest")]
        public string? Digest { get; set; } = null;
    }
}
