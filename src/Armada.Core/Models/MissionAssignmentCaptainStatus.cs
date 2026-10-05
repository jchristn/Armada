namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// What one captain is doing, as part of a <see cref="MissionAssignmentBlocker"/> that explains why no captain is
    /// free for a Pending mission.
    /// </summary>
    public class MissionAssignmentCaptainStatus
    {
        #region Public-Members

        /// <summary>
        /// Captain ID.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Captain name.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Captain state.
        /// </summary>
        public CaptainStateEnum State { get; set; } = CaptainStateEnum.Idle;

        /// <summary>
        /// Human-readable description (English), for example "refining backlog item 'Add retries'".
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// Mission the captain is working on, when Working.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Planning session holding the captain, when Planning.
        /// </summary>
        public string? PlanningSessionId { get; set; } = null;

        /// <summary>
        /// Backlog refinement session holding the captain, when Refining.
        /// </summary>
        public string? RefinementSessionId { get; set; } = null;

        /// <summary>
        /// Backlog item (objective) being refined, when Refining.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// When the captain's quarantine ends, when Quarantined.
        /// </summary>
        public DateTime? QuarantineUntilUtc { get; set; } = null;

        /// <summary>
        /// Why the captain was quarantined, when Quarantined.
        /// </summary>
        public string? QuarantineReason { get; set; } = null;

        #endregion
    }
}
