namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;
    using Armada.Runtimes.Mcp;

    /// <summary>
    /// A JSON-RPC request received by <see cref="McpStubServerHandler"/>, deserialized, plus the MCP session header it carried.
    /// </summary>
    public sealed class McpStubRequest
    {
        #region Public-Members

        /// <summary>
        /// Request id in canonical string form, or null for a notification.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(JsonRpcIdConverter))]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Method name.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// Parameters, or null.
        /// </summary>
        [JsonPropertyName("params")]
        public McpStubRequestParams? Params { get; set; } = null;

        /// <summary>
        /// Value of the Mcp-Session-Id request header, or null.
        /// </summary>
        [JsonIgnore]
        public string? SessionId { get; set; } = null;

        #endregion
    }
}
