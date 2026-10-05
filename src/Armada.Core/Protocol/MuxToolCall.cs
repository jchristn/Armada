namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The tool call of a Mux <c>tool_call_proposed</c> event.
    /// </summary>
    public class MuxToolCall
    {
        #region Public-Members

        /// <summary>
        /// Tool call identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Arguments as raw JSON (an object, or a string when Mux could not parse them), or null.
        /// </summary>
        [JsonPropertyName("arguments")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Arguments { get; set; } = null;

        #endregion
    }
}
