namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// Result of the MCP papercut_summary tool: how many stored reports were scanned and matched, and at most
    /// <see cref="MaxRows"/> groups (or, ungrouped, reports).
    /// </summary>
    public class PapercutSummaryResult
    {
        #region Public-Members

        /// <summary>
        /// Most rows returned (the tool's limit argument is clamped to 1 to this).
        /// </summary>
        public const int MaxRows = 200;

        /// <summary>
        /// Stored reports scanned (the scanLimit argument, 1 to 5000).
        /// </summary>
        public int Scanned { get; set; } = 0;

        /// <summary>
        /// Reports that matched the filters.
        /// </summary>
        public int Matched { get; set; } = 0;

        /// <summary>
        /// Number of groups the matches formed (grouped mode), or null.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? GroupCount { get; set; } = null;

        /// <summary>
        /// Groups, most frequent first (grouped mode), or null.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CollectionBound(MaxRows, Reason = "The limit argument is clamped to MaxRows")]
        public List<PapercutGroup>? Groups { get; set; } = null;

        /// <summary>
        /// Reports, newest first (ungrouped mode), or null.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [CollectionBound(MaxRows, Reason = "The limit argument is clamped to MaxRows")]
        public List<Papercut>? Papercuts { get; set; } = null;

        #endregion
    }
}
