namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>part</c> of an OpenCode <c>--format json</c> event.
    /// </summary>
    public class OpenCodePart
    {
        #region Public-Members

        /// <summary>
        /// Part identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Part type (text, reasoning, tool, step-start, step-finish).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a text or reasoning part, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Tool name of a tool part, or null.
        /// </summary>
        [JsonPropertyName("tool")]
        public string? Tool { get; set; } = null;

        /// <summary>
        /// Tool call identifier of a tool part, or null.
        /// </summary>
        [JsonPropertyName("callID")]
        public string? CallId { get; set; } = null;

        /// <summary>
        /// Tool state of a tool part, or null.
        /// </summary>
        [JsonPropertyName("state")]
        public OpenCodeToolState? State { get; set; } = null;

        /// <summary>
        /// Token usage of the step on a step-finish part, or null.
        /// </summary>
        [JsonPropertyName("tokens")]
        public OpenCodeTokens? Tokens { get; set; } = null;

        /// <summary>
        /// Cost of the step in US dollars on a step-finish part, or null.
        /// </summary>
        [JsonPropertyName("cost")]
        public double? Cost { get; set; } = null;

        #endregion
    }
}
