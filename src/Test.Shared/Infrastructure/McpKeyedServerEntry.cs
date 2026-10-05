namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One server inside <see cref="McpKeyedConfigFile"/>.
    /// </summary>
    public class McpKeyedServerEntry
    {
        #region Public-Members

        /// <summary>
        /// Transport type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Endpoint URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Endpoint URL under the Gemini key name, or null.
        /// </summary>
        [JsonPropertyName("httpUrl")]
        public string? HttpUrl { get; set; } = null;

        /// <summary>
        /// Extra HTTP headers, or null.
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; } = null;

        #endregion
    }
}
