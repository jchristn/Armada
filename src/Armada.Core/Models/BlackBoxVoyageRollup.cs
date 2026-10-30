namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Voyage section (voyage entries only): missions by status, failed entries, cascade root and settlement.
    /// </summary>
    public class BlackBoxVoyageRollup
    {
        /// <summary>
        /// Missions in the voyage.
        /// </summary>
        public int MissionTotal { get; set; } = 0;

        /// <summary>
        /// Missions by status.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxVoyageStatusCounts, Reason = "One per MissionStatusEnum value")]
        public List<BlackBoxStatusCount> MissionsByStatus
        {
            get => _MissionsByStatus;
            set => _MissionsByStatus = value ?? new List<BlackBoxStatusCount>();
        }

        /// <summary>
        /// Failed missions.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxVoyageFailedMissions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> FailedMissionIds
        {
            get => _FailedMissionIds;
            set => _FailedMissionIds = value ?? new List<string>();
        }

        /// <summary>
        /// Current black box entries of the failed missions.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxVoyageFailedMissions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> FailedMissionEntryIds
        {
            get => _FailedMissionEntryIds;
            set => _FailedMissionEntryIds = value ?? new List<string>();
        }

        /// <summary>
        /// Root failed mission, or null.
        /// </summary>
        public string? CascadeRootMissionId { get; set; } = null;

        /// <summary>
        /// Root failed mission's entry, or null.
        /// </summary>
        public string? CascadeRootEntryId { get; set; } = null;

        /// <summary>
        /// Settlement rule that decided the voyage, or null.
        /// </summary>
        public string? SettlementRule { get; set; } = null;

        /// <summary>
        /// Halt reason, or null.
        /// </summary>
        public string? HaltReason { get; set; } = null;

        private List<BlackBoxStatusCount> _MissionsByStatus = new List<BlackBoxStatusCount>();
        private List<string> _FailedMissionIds = new List<string>();
        private List<string> _FailedMissionEntryIds = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxVoyageRollup()
        {
        }
    }
}
