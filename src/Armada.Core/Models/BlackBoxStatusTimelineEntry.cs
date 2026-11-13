namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One status change in a black box status timeline.
    /// </summary>
    public class BlackBoxStatusTimelineEntry
    {
        /// <summary>
        /// Status name.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// When (UTC).
        /// </summary>
        public DateTime Utc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// What set it (event type or component), or null.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStatusTimelineEntry()
        {
        }
    }
}
