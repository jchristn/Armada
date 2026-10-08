namespace Armada.Core.Metrics
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The recorded data <see cref="HarborMetricsBuilder"/> turns into a <see cref="HarborMetrics"/> response.
    /// </summary>
    public class HarborMetricsInput
    {
        #region Public-Members

        /// <summary>
        /// The Harbor.
        /// </summary>
        public Harbor Harbor { get; set; } = new Harbor();

        /// <summary>
        /// The requested window.
        /// </summary>
        public HarborMetricsRangeEnum Range { get; set; } = HarborMetricsRangeEnum.OneDay;

        /// <summary>
        /// The current time (UTC).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// How long a closed link counts as reconnecting before it counts as down.
        /// </summary>
        public TimeSpan ReconnectGrace { get; set; } = TimeSpan.FromSeconds(45);

        /// <summary>
        /// The Harbor's jobs that were active or ended in the window.
        /// </summary>
        public List<HarborJobRecord> Jobs { get; set; } = new List<HarborJobRecord>();

        /// <summary>
        /// The Harbor's link samples in the window.
        /// </summary>
        public List<HarborLinkSample> Samples { get; set; } = new List<HarborLinkSample>();

        /// <summary>
        /// The Harbor's most recent link sample (for its current reconnect counters), or null.
        /// </summary>
        public HarborLinkSample? LatestSample { get; set; } = null;

        /// <summary>
        /// The Harbor's link events in the window, oldest first.
        /// </summary>
        public List<HarborLinkEvent> Events { get; set; } = new List<HarborLinkEvent>();

        /// <summary>
        /// The Harbor's latest link event before the window, or null.
        /// </summary>
        public HarborLinkEvent? EventBeforeWindow { get; set; } = null;

        /// <summary>
        /// Token-usage records attributed to the Harbor in the window, already scoped to what the caller may see.
        /// </summary>
        public List<TokenUsageRecord> TokenRecords { get; set; } = new List<TokenUsageRecord>();

        #endregion
    }
}
