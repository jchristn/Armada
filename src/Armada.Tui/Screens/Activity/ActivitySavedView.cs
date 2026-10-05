namespace Armada.Tui.Screens.Activity
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// A saved All Activity view (the dashboard's saved history view): a name and the timeline query it applies.
    /// </summary>
    public class ActivitySavedView
    {
        #region Public-Members

        /// <summary>
        /// Id (<c>hsv_</c> prefix).
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Filters the view applies.
        /// </summary>
        public HistoricalTimelineQuery Query
        {
            get { return _Query; }
            set { _Query = value ?? new HistoricalTimelineQuery(); }
        }

        /// <summary>
        /// When the view was saved.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private HistoricalTimelineQuery _Query = new HistoricalTimelineQuery();

        #endregion
    }
}
