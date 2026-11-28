namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A validated submission stored on a mission attempt (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2). The latest submission of
    /// the attempt decides the stage's plan, verdict or result; marker text in the output is the fallback.
    /// </summary>
    public class StageSubmission
    {
        #region Public-Members

        /// <summary>
        /// Id, prefix <c>sbm_</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Mission.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// The attempt, as <see cref="HandoffArtifact.AttemptIdFor"/> builds it.
        /// </summary>
        public string AttemptId { get; set; } = "";

        /// <summary>
        /// Persona of the mission when it submitted.
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// What was submitted.
        /// </summary>
        public StageSubmissionKindEnum Kind { get; set; } = StageSubmissionKindEnum.Result;

        /// <summary>
        /// How it arrived.
        /// </summary>
        public StageSubmissionSourceEnum Source { get; set; } = StageSubmissionSourceEnum.Mcp;

        /// <summary>
        /// The validated submission.
        /// </summary>
        public StageSubmissionEnvelope Submission { get; set; } = new StageSubmissionEnvelope();

        /// <summary>
        /// When it was stored (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.StageSubmissionIdPrefix, 24);

        #endregion
    }
}
