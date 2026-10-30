namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One overlap found at dispatch (or in an overlap preview) between a new mission and an active mission, a
    /// black box hotspot, or a reserved directory on the same vessel.
    /// </summary>
    public class MissionOverlapWarning
    {
        #region Public-Members

        /// <summary>
        /// What the new mission overlaps.
        /// </summary>
        public MissionOverlapKindEnum Kind { get; set; } = MissionOverlapKindEnum.ActiveMission;

        /// <summary>
        /// The new mission (msn_ prefix) the warning is about, or null in a preview.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The active mission it overlaps (ActiveMission and Reservation), or null.
        /// </summary>
        public string? OverlappingMissionId { get; set; } = null;

        /// <summary>
        /// Title of the overlapping mission, or null.
        /// </summary>
        public string? OverlappingMissionTitle { get; set; } = null;

        /// <summary>
        /// Status of the overlapping mission, or null.
        /// </summary>
        public string? OverlappingMissionStatus { get; set; } = null;

        /// <summary>
        /// The hotspot or reserved directory (Hotspot and Reservation), or null.
        /// </summary>
        public string? Directory { get; set; } = null;

        /// <summary>
        /// Black box entries that recorded conflicts in the directory (Hotspot and Reservation).
        /// </summary>
        public long HotspotEntryCount { get; set; } = 0;

        /// <summary>
        /// The overlapping files (or the new mission's files in the directory), first 20.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxWarningFiles, Reason = "First files of the overlap; FileCount has the total")]
        public List<string> Files
        {
            get => _Files;
            set => _Files = value ?? new List<string>();
        }

        /// <summary>
        /// How many files overlap.
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// True when dispatch chained the new mission after OverlappingMissionId (Vessel.SerializeOverlappingMissions).
        /// </summary>
        public bool Serialized { get; set; } = false;

        /// <summary>
        /// One sentence for people.
        /// </summary>
        public string Message { get; set; } = "";

        #endregion

        #region Private-Members

        private List<string> _Files = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionOverlapWarning()
        {
        }

        #endregion
    }
}
