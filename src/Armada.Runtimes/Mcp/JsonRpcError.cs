namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// The <c>error</c> member of a JSON-RPC 2.0 response.
    /// </summary>
    public class JsonRpcError
    {
        #region Public-Members

        /// <summary>
        /// JSON-RPC error code (for example -32601 method not found, -32602 invalid params).
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; } = 0;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Optional error data as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("data")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Data { get; set; } = null;

        #endregion
    }
}
