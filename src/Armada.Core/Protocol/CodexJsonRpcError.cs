namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Error object of a Codex app-server JSON-RPC response.
    /// </summary>
    public class CodexJsonRpcError
    {
        #region Public-Members

        /// <summary>
        /// JSON-RPC error code, or null.
        /// </summary>
        [JsonPropertyName("code")]
        public long? Code { get; set; } = null;

        /// <summary>
        /// Error message, or null. Redacted before it is stored anywhere.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        #endregion
    }
}
