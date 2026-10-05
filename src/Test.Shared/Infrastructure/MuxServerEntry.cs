namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One server inside <see cref="MuxServersFile"/>.
    /// </summary>
    public class MuxServerEntry
    {
        #region Public-Members

        /// <summary>
        /// Server name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Transport.
        /// </summary>
        [JsonPropertyName("transport")]
        public string? Transport { get; set; } = null;

        /// <summary>
        /// Base URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// MCP path under the base URL.
        /// </summary>
        [JsonPropertyName("mcpPath")]
        public string? McpPath { get; set; } = null;

        /// <summary>
        /// Authentication block, or null.
        /// </summary>
        [JsonPropertyName("auth")]
        public MuxServerAuth? Auth { get; set; } = null;

        #endregion
    }
}
