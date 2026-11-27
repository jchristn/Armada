namespace Armada.Core.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Payload of an ApiEndpoint runtime "[tool-lifecycle] " output line: which tool call started or ended.
    /// </summary>
    public sealed class ToolLifecycleLine
    {
        /// <summary>
        /// Tool call id, or null when the line carries none.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// True when the call started, false when it ended, null when the line does not say.
        /// </summary>
        [JsonPropertyName("started")]
        public bool? Started { get; set; } = null;
    }
}
