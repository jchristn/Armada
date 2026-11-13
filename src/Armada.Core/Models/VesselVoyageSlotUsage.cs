namespace Armada.Core.Models
{
    using System;

    /// <summary>A voyage (or standalone mission chain) holding one of a vessel's voyage slots.</summary>
    public class VesselVoyageSlotUsage
    {
        /// <summary>The voyage, or null for a standalone mission chain.</summary>
        public string? VoyageId { get; set; }

        /// <summary>Standalone chain root when <see cref="VoyageId"/> is null.</summary>
        public string? MissionId { get; set; }

        /// <summary>Voyage or mission title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>Missions of this voyage running on the vessel (Assigned or InProgress).</summary>
        public int RunningMissions { get; set; }

        /// <summary>Missions of this voyage still Pending.</summary>
        public int PendingMissions { get; set; }
    }
}
