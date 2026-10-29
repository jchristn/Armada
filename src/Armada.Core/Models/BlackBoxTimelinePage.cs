namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One page of an entry's combined timeline, ordered by time (oldest first by default) with the source and id breaking
    /// ties.
    /// </summary>
    public class BlackBoxTimelinePage : EnumerationResult<BlackBoxTimelineItem>
    {
        #region Public-Members

        /// <summary>
        /// Entry (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Start of the window (the mission's or voyage's creation).
        /// </summary>
        public DateTime? WindowStartUtc { get; set; } = null;

        /// <summary>
        /// End of the window (the entry's last update).
        /// </summary>
        public DateTime? WindowEndUtc { get; set; } = null;

        /// <summary>
        /// Items each source contributed.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxTimelineSources, Reason = "One per BlackBoxTimelineSourceEnum value")]
        public List<BlackBoxTimelineSourceCount> Sources
        {
            get => _Sources;
            set => _Sources = value ?? new List<BlackBoxTimelineSourceCount>();
        }

        /// <summary>
        /// True when a source was cut at its cap or the timeline at 1000 items.
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Private-Members

        private List<BlackBoxTimelineSourceCount> _Sources = new List<BlackBoxTimelineSourceCount>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTimelinePage()
        {
        }

        #endregion
    }
}
