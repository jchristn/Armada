namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A directory that keeps appearing in black box entries of a vessel.
    /// </summary>
    public class BlackBoxHotspot
    {
        /// <summary>
        /// Vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Directory (empty for the root).
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// File rows recorded under it.
        /// </summary>
        public long FileCount { get; set; } = 0;

        /// <summary>
        /// Distinct entries.
        /// </summary>
        public long EntryCount { get; set; } = 0;

        /// <summary>
        /// Newest file row (UTC).
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxHotspot()
        {
        }
    }
}
