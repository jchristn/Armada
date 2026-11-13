namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Body of Ollama's GET /api/tags (the models installed on the server), read for a stage outcome's model digest.
    /// </summary>
    public class OllamaTagsResponse
    {
        /// <summary>
        /// Installed models, or null.
        /// </summary>
        [JsonPropertyName("models")]
        public List<OllamaTagModel>? Models { get; set; } = null;
    }
}
