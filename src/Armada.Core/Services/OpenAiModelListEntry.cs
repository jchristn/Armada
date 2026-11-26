namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One model in an OpenAI-compatible GET /v1/models body.
    /// </summary>
    public class OpenAiModelListEntry
    {
        /// <summary>
        /// Model identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// The context window the server runs the model with (vLLM), or null on servers that do not report it.
        /// </summary>
        [JsonPropertyName("max_model_len")]
        public int? MaxModelLen { get; set; } = null;
    }
}
