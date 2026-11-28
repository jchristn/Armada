namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The part of Ollama's POST /api/show body that context discovery reads.
    /// </summary>
    public class OllamaShowResponse
    {
        /// <summary>
        /// The model's Modelfile parameters, one "name value" pair per line (for example "num_ctx 32768"), or null.
        /// </summary>
        [JsonPropertyName("parameters")]
        public string? Parameters { get; set; } = null;
    }
}
