namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An OpenAI-compatible GET /v1/models body.
    /// </summary>
    public class OpenAiModelListResponse
    {
        /// <summary>
        /// Models the server lists, or null.
        /// </summary>
        [JsonPropertyName("data")]
        public List<OpenAiModelListEntry>? Data { get; set; } = null;
    }
}
