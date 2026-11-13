namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A typed Codex app-server JSON-RPC response.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public class CodexJsonRpcResponse<T> where T : class
    {
        #region Public-Members

        /// <summary>
        /// Request id this response answers, or null.
        /// </summary>
        [JsonPropertyName("id")]
        public long? Id { get; set; } = null;

        /// <summary>
        /// Typed result, or null.
        /// </summary>
        [JsonPropertyName("result")]
        public T? Result { get; set; } = null;

        /// <summary>
        /// Error object when the request failed, or null.
        /// </summary>
        [JsonPropertyName("error")]
        public CodexJsonRpcError? Error { get; set; } = null;

        #endregion
    }
}
