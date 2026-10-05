namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;
    using Armada.Runtimes.Mcp;

    /// <summary>
    /// A JSON-RPC request received by <see cref="ToolClientStubHandler"/>.
    /// </summary>
    public sealed class ToolClientStubRequest
    {
        #region Public-Members

        /// <summary>
        /// Request id in canonical string form, or null for a notification.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(JsonRpcIdConverter))]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Method.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// Params, or null.
        /// </summary>
        [JsonPropertyName("params")]
        public ToolClientStubParams? Params { get; set; } = null;

        #endregion
    }
}
