namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Payload of a <c>mission.landing_reconciled</c> event: the proof that a LandingFailed mission's work is on the
    /// remote target branch, recorded when the landing reconciler moves the mission to Complete.
    /// </summary>
    public class LandingReconciliationEvidence
    {
        #region Public-Members

        /// <summary>
        /// The reconciled mission.
        /// </summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>
        /// The mission's vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// The commit on the target branch that proves the landing.
        /// </summary>
        public string EvidenceCommit { get; set; } = String.Empty;

        /// <summary>
        /// How the commit was found.
        /// </summary>
        public LandingEvidenceKindEnum EvidenceKind { get; set; } = LandingEvidenceKindEnum.CommitAncestry;

        /// <summary>
        /// The ref the commit was found on (for example refs/remotes/origin/main).
        /// </summary>
        public string TargetRef { get; set; } = String.Empty;

        /// <summary>
        /// The Harbor whose checkout was inspected, or null for a repository on the Admiral host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// The landing failure reason the mission carried before it was reconciled.
        /// </summary>
        public string? PreviousFailureReason { get; set; } = null;

        /// <summary>
        /// When the mission was reconciled.
        /// </summary>
        public DateTime ReconciledUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
