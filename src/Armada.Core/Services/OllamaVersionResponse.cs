namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of Ollama's GET /api/version, read for a stage outcome's runtime version.
    /// </summary>
    public class OllamaVersionResponse
    {
        /// <summary>
        /// Ollama server version (for example "0.12.3"), or null.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; } = null;
    }
}
