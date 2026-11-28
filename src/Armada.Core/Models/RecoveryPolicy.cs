namespace Armada.Core.Models
{
    /// <summary>
    /// The recovery policy in effect for a captain's missions (RS-5.5): the global settings with the captain's model
    /// profile overrides applied. Produced by <see cref="Armada.Core.Services.RecoveryPolicyResolver"/>.
    /// </summary>
    public class RecoveryPolicy
    {
        /// <summary>
        /// The model profile whose overrides were applied, or null when only the global settings apply.
        /// </summary>
        public string? ModelProfileId { get; set; } = null;

        /// <summary>
        /// In-place resumes after a transient or task failure.
        /// </summary>
        public int MaxResumes { get; set; } = 0;

        /// <summary>
        /// In-place resumes after an Unclassified exit, never more than <see cref="MaxResumes"/>.
        /// </summary>
        public int UnclassifiedResumes { get; set; } = 0;

        /// <summary>
        /// Minutes a transient endpoint outage is ridden out past the resumes.
        /// </summary>
        public int TransientGraceMinutes { get; set; } = 0;

        /// <summary>
        /// Automatic Judge rework rounds for a Worker slice.
        /// </summary>
        public int JudgeReworkRounds { get; set; } = 1;

        /// <summary>
        /// Output-contract repair turns per mission.
        /// </summary>
        public int OutputRepairAttempts { get; set; } = 0;

        /// <summary>
        /// Resumes after which a task or Unclassified failure escalates instead of resuming (RS-5.6). Equals
        /// <see cref="MaxResumes"/> unless the profile sets a lower value.
        /// </summary>
        public int EscalateAfterResumes { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RecoveryPolicy()
        {
        }
    }
}
