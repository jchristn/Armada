namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Routing fields of one Codex app-server JSON-RPC line (the server omits the jsonrpc header): a response has an id, a notification or server request has a method.
    /// </summary>
    public class CodexJsonRpcEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Request id this line answers, or null for a notification.
        /// </summary>
        [JsonPropertyName("id")]
        public long? Id { get; set; } = null;

        /// <summary>
        /// Notification or server-request method, or null for a response.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// Error object when the request failed, or null.
        /// </summary>
        [JsonPropertyName("error")]
        public CodexJsonRpcError? Error { get; set; } = null;

        #endregion
    }
}
