namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// One Worker eligibility envelope decision for a mission, recorded as the payload of a
    /// <c>mission.worker_envelope</c> event (mission timeline and black box).
    /// </summary>
    public class WorkerEnvelopeDecision
    {
        #region Public-Members

        /// <summary>
        /// Mission identifier.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// What dispatch decided.
        /// </summary>
        public WorkerEnvelopeOutcomeEnum Outcome { get; set; } = WorkerEnvelopeOutcomeEnum.Waiting;

        /// <summary>
        /// Captain assigned (Assigned or Escalated), or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Name of the captain assigned, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Effective tier of the escalated captain, or null.
        /// </summary>
        public CaptainTierEnum? CaptainTier { get; set; } = null;

        /// <summary>
        /// Highest tier among the captains normal routing would have used that meet the envelope (an escalation picks a
        /// stronger one), or null when none does.
        /// </summary>
        public CaptainTierEnum? BaselineTier { get; set; } = null;

        /// <summary>
        /// The effective envelope (the vessel default tightened by the mission's own).
        /// </summary>
        public WorkerEnvelope Envelope { get; set; } = new WorkerEnvelope();

        /// <summary>
        /// Generated description of the envelope.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Captains of the mission's tenant the envelope excluded, and why.
        /// </summary>
        public List<WorkerEnvelopeExclusion> Excluded { get; set; } = new List<WorkerEnvelopeExclusion>();

        /// <summary>
        /// Short English summary, the event message.
        /// </summary>
        public string Summary { get; set; } = "";

        #endregion
    }
}
