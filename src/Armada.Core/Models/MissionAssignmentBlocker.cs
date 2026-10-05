namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Server-computed explanation of why a Pending mission is still waiting for a captain. Computed when the mission is
    /// read (not stored); null for missions that are not Pending.
    /// </summary>
    public class MissionAssignmentBlocker
    {
        #region Public-Members

        /// <summary>
        /// Typed reason.
        /// </summary>
        public MissionAssignmentBlockerReasonEnum Reason { get; set; } = MissionAssignmentBlockerReasonEnum.AwaitingDispatch;

        /// <summary>
        /// Human-readable summary (English), for example "Waiting for a captain: every captain is busy."
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// Earliest time the blocker is expected to clear on its own (for example when a quarantine ends), or null.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// The mission this one waits for, for dependency reasons.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// Active missions on the vessel that hold it, for vessel concurrency and broad-scope reasons.
        /// </summary>
        public List<string> BlockingMissionIds { get; set; } = new List<string>();

        /// <summary>
        /// What each captain is doing, for captain reasons. Only captains visible to the mission's tenant are listed.
        /// </summary>
        public List<MissionAssignmentCaptainStatus> Captains { get; set; } = new List<MissionAssignmentCaptainStatus>();

        /// <summary>
        /// When this explanation was computed.
        /// </summary>
        public DateTime ComputedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
