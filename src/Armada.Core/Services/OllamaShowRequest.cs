namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama's POST /api/show request body.
    /// </summary>
    public class OllamaShowRequest
    {
        /// <summary>
        /// Model to describe.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
    }
}
