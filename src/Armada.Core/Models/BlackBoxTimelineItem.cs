namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One item of a combined black box timeline.
    /// </summary>
    public class BlackBoxTimelineItem
    {
        #region Public-Members

        /// <summary>
        /// When it happened.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Where it comes from.
        /// </summary>
        public BlackBoxTimelineSourceEnum Source { get; set; } = BlackBoxTimelineSourceEnum.Mission;

        /// <summary>
        /// Event type (for example mission.status_changed) or a row milestone (for example mission.created, dock.created, entry.captured).
        /// </summary>
        public string Kind { get; set; } = "";

        /// <summary>
        /// What happened (clipped to 500 characters).
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// The row the item is about (mission, dock, merge entry, captain, Harbor, event or note id), or null.
        /// </summary>
        public string? EntityId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTimelineItem()
        {
        }

        #endregion
    }
}
