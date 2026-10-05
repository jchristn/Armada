namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One MCP server in an <see cref="OpenCodeConfigFile"/>.
    /// </summary>
    public class OpenCodeMcpEntry
    {
        #region Public-Members

        /// <summary>
        /// Server type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Headers, or null.
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; } = null;

        /// <summary>
        /// Whether the server is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; } = null;

        #endregion
    }
}
