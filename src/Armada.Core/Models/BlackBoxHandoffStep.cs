namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One step of a record's handoff and landing trail: a mission, dock, merge entry or landing event.
    /// </summary>
    public class BlackBoxHandoffStep
    {
        /// <summary>
        /// When (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source (Mission, Dock or MergeEntry).
        /// </summary>
        public BlackBoxTimelineSourceEnum Source { get; set; } = BlackBoxTimelineSourceEnum.Mission;

        /// <summary>
        /// Event type (for example mission.landing_retry or dock.reclaimed).
        /// </summary>
        public string Kind { get; set; } = "";

        /// <summary>
        /// Message (at most 500 characters), or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Entity the step is about (event, dock or merge entry id), or null.
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxHandoffStep()
        {
        }
    }
}
