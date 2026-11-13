namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One loaded model in Ollama's GET /api/ps.
    /// </summary>
    public class OllamaPsModel
    {
        /// <summary>
        /// Model name, for example qwen3:8b.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Model reference.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Context length the loaded model serves, or null on an Ollama that does not report it.
        /// </summary>
        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; } = null;
    }
}
