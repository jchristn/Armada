namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// Parameters of an MCP <c>tools/call</c> request.
    /// </summary>
    public class McpToolCallParams
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as a serialized JSON object, written verbatim.
        /// </summary>
        [JsonPropertyName("arguments")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Arguments { get; set; } = "{}";

        #endregion
    }
}
