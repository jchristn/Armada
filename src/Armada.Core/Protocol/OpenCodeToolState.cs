namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The state of an OpenCode tool part.
    /// </summary>
    public class OpenCodeToolState
    {
        #region Public-Members

        /// <summary>
        /// Status (pending, running, completed, error).
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; } = null;

        /// <summary>
        /// Tool input as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string? Input { get; set; } = null;

        /// <summary>
        /// Tool output (text, or raw JSON when structured), or null.
        /// </summary>
        [JsonPropertyName("output")]
        [JsonConverter(typeof(TextOrRawJsonConverter))]
        public string? Output { get; set; } = null;

        /// <summary>
        /// Error text when the status is error, or null.
        /// </summary>
        [JsonPropertyName("error")]
        [JsonConverter(typeof(TextOrRawJsonConverter))]
        public string? Error { get; set; } = null;

        /// <summary>
        /// Metadata, or null.
        /// </summary>
        [JsonPropertyName("metadata")]
        public OpenCodeToolMetadata? Metadata { get; set; } = null;

        #endregion
    }
}
