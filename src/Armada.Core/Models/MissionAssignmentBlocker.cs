namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Server-computed explanation of why a Pending mission is still waiting for a captain. Computed when the mission is
    /// read (not stored); null for missions that are not Pending.
    /// </summary>
    public class MissionAssignmentBlocker
    {
        #region Public-Members

        /// <summary>Optional stable usage-selection subreason for clients that support localized selection diagnostics.</summary>
        public string? SelectionReasonCode { get; set; } = null;

        /// <summary>
        /// Typed reason.
        /// </summary>
        public MissionAssignmentBlockerReasonEnum Reason { get; set; } = MissionAssignmentBlockerReasonEnum.AwaitingDispatch;

        /// <summary>
        /// Human-readable summary (English), for example "Waiting for a captain: every captain is busy."
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>Bounded next steps computed from the same limits as admission.</summary>
        [CollectionBound(MaxListed, Reason = "At most MaxListed actionable next steps")]
        public List<AssignmentBlockerAdvice> Advice { get; set; } = new List<AssignmentBlockerAdvice>();

        /// <summary>
        /// Earliest time the blocker is expected to clear on its own (for example when a quarantine ends), or null.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// The mission this one waits for, for dependency reasons.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// Active missions on the vessel that hold it, for vessel concurrency and broad-scope reasons: the first
        /// <see cref="MaxListed"/>. <see cref="BlockingMissionCount"/> is the full count.
        /// </summary>
        [CollectionBound(MaxListed, Reason = "The first MaxListed holders; BlockingMissionCount carries the full count")]
        public List<string> BlockingMissionIds { get; set; } = new List<string>();

        /// <summary>
        /// Number of missions holding the vessel (may exceed the ids listed in <see cref="BlockingMissionIds"/>).
        /// </summary>
        public int BlockingMissionCount { get; set; } = 0;

        /// <summary>
        /// What each captain is doing, for captain reasons: the first <see cref="MaxListed"/> captains by name. Only
        /// captains visible to the mission's tenant are listed. <see cref="CaptainCount"/> is the full count.
        /// </summary>
        [CollectionBound(MaxListed, Reason = "The first MaxListed captains by name; CaptainCount carries the full count")]
        public List<MissionAssignmentCaptainStatus> Captains { get; set; } = new List<MissionAssignmentCaptainStatus>();

        /// <summary>
        /// Number of captains visible to the mission's tenant (may exceed the entries in <see cref="Captains"/>).
        /// </summary>
        public int CaptainCount { get; set; } = 0;

        /// <summary>
        /// Most entries listed in <see cref="BlockingMissionIds"/> and <see cref="Captains"/>.
        /// </summary>
        public const int MaxListed = 25;

        /// <summary>
        /// When this explanation was computed.
        /// </summary>
        public DateTime ComputedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the blocking missions: the full count and the first <see cref="MaxListed"/> ids.
        /// </summary>
        /// <param name="missionIds">Every blocking mission id, in display order.</param>
        public void SetBlockingMissions(IEnumerable<string>? missionIds)
        {
            List<string> all = missionIds == null ? new List<string>() : new List<string>(missionIds);
            BlockingMissionCount = all.Count;
            BlockingMissionIds = all.Count <= MaxListed ? all : all.GetRange(0, MaxListed);
        }

        #endregion
    }
}
