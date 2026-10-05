namespace Armada.Runtimes.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The routing members of a JSON-RPC 2.0 message (<c>id</c> and <c>method</c>), used to select the response to a given
    /// request out of a stream that may also carry notifications and progress messages.
    /// </summary>
    public class JsonRpcEnvelopeHeader
    {
        #region Public-Members

        /// <summary>
        /// The id in canonical string form, or null.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonConverter(typeof(JsonRpcIdConverter))]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Method name for a notification or request; null for a response.
        /// </summary>
        [JsonPropertyName("method")]
        public string? Method { get; set; } = null;

        #endregion
    }
}
