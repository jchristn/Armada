namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// The JSON export of All Activity (same shape as the dashboard's history export).
    /// </summary>
    public class ActivityExportDocument
    {
        #region Public-Members

        /// <summary>
        /// Export time.
        /// </summary>
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Query used.
        /// </summary>
        public HistoricalTimelineQuery Query { get; set; } = new HistoricalTimelineQuery();

        /// <summary>
        /// Number of entries.
        /// </summary>
        public int TotalCount { get; set; } = 0;

        /// <summary>
        /// Entries.
        /// </summary>
        public List<HistoricalTimelineEntry> Entries { get; set; } = new List<HistoricalTimelineEntry>();

        #endregion
    }
}
