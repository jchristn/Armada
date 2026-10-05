namespace Armada.Runtimes.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON-RPC 2.0 message as received from a peer, typed by its <c>result</c>. A response carries an <c>id</c> and
    /// either <c>result</c> or <c>error</c>; a notification or server-to-client request carries a <c>method</c> instead.
    /// </summary>
    /// <typeparam name="T">The type of the <c>result</c> member.</typeparam>
    public class JsonRpcResponse<T> where T : class
    {
        #region Public-Members

        /// <summary>
        /// Protocol version marker (2.0).
        /// </summary>
        [JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; } = null;

        /// <summary>
        /// The request id this message answers, in canonical string form, or null for a notification.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(JsonRpcIdConverter))]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Method name when the message is a notification or a server-to-client request rather than a response.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        /// <summary>
        /// The result, or null.
        /// </summary>
        [JsonPropertyName("result")]
        public T? Result { get; set; } = null;

        /// <summary>
        /// The error, or null.
        /// </summary>
        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; } = null;

        /// <summary>
        /// True when this message is a response (no method; carries an id).
        /// </summary>
        [JsonIgnore]
        public bool IsResponse => Method == null && Id != null;

        #endregion
    }
}
