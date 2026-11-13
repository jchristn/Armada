namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One window of a Codex app-server rate-limit snapshot. The duration is data: a window is never assumed to be 5 hours or 7 days.
    /// </summary>
    public class CodexRateLimitWindow
    {
        #region Public-Members

        /// <summary>
        /// Percent of the window used (schema: required int32), or null when absent.
        /// </summary>
        [JsonPropertyName("usedPercent")]
        public double? UsedPercent { get; set; } = null;

        /// <summary>
        /// Window length in minutes, or null.
        /// </summary>
        [JsonPropertyName("windowDurationMins")]
        public long? WindowDurationMins { get; set; } = null;

        /// <summary>
        /// Absolute reset time in Unix epoch seconds, or null.
        /// </summary>
        [JsonPropertyName("resetsAt")]
        public long? ResetsAt { get; set; } = null;

        #endregion
    }
}
