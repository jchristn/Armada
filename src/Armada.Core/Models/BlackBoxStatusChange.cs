namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Status section: the subject's status before and after the failure write, and its timeline.
    /// </summary>
    public class BlackBoxStatusChange
    {
        /// <summary>
        /// Status before the failure write (MissionStatusEnum or VoyageStatusEnum name), or null when unknown.
        /// </summary>
        public string? Before { get; set; } = null;

        /// <summary>
        /// Status after the failure write.
        /// </summary>
        public string? After { get; set; } = null;

        /// <summary>
        /// When the status changed (UTC).
        /// </summary>
        public DateTime? ChangedUtc { get; set; } = null;

        /// <summary>
        /// Status changes in the attempt, oldest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTimeline, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxStatusTimelineEntry> Timeline
        {
            get => _Timeline;
            set => _Timeline = value ?? new List<BlackBoxStatusTimelineEntry>();
        }

        private List<BlackBoxStatusTimelineEntry> _Timeline = new List<BlackBoxStatusTimelineEntry>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStatusChange()
        {
        }
    }
}
