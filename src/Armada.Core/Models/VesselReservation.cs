namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A soft directory reservation: an active mission whose scoped files sit in a directory the vessel's black box marks
    /// hot (at least blackBox.hotspotReservationThreshold entries). A second mission touching the directory is warned,
    /// or chained when the vessel serializes overlapping missions.
    /// </summary>
    public class VesselReservation
    {
        #region Public-Members

        /// <summary>
        /// Vessel (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// The reserved directory (repository-relative, no trailing slash; empty for the repository root).
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// The reserving mission (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Title of the reserving mission.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Status of the reserving mission.
        /// </summary>
        public string? MissionStatus { get; set; } = null;

        /// <summary>
        /// Black box entries that recorded conflicts in the directory.
        /// </summary>
        public long HotspotEntryCount { get; set; } = 0;

        /// <summary>
        /// Scoped files of the mission in the directory.
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// When the reservation started (the mission's start, or its creation while Pending).
        /// </summary>
        public DateTime SinceUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselReservation()
        {
        }

        #endregion
    }
}
