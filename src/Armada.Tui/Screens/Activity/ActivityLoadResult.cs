namespace Armada.Tui.Screens.Activity
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What one All Activity load returns: timeline entries plus the vessel and backlog item picker data.
    /// </summary>
    public class ActivityLoadResult
    {
        #region Public-Members

        /// <summary>
        /// Timeline entries.
        /// </summary>
        public List<HistoricalTimelineEntry> Entries { get; set; } = new List<HistoricalTimelineEntry>();

        /// <summary>
        /// Vessels.
        /// </summary>
        public List<Vessel> Vessels { get; set; } = new List<Vessel>();

        /// <summary>
        /// Backlog items.
        /// </summary>
        public List<Objective> Objectives { get; set; } = new List<Objective>();

        #endregion
    }
}
