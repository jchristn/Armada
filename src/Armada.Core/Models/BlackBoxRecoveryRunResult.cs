namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a recovery dry run or run.
    /// </summary>
    public class BlackBoxRecoveryRunResult
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Recovery path.
        /// </summary>
        public string PathId { get; set; } = "";

        /// <summary>
        /// Whether this was a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Confirm token (dry run only).
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        /// <summary>
        /// When the token expires (UTC).
        /// </summary>
        public DateTime? ConfirmTokenExpiresUtc { get; set; } = null;

        /// <summary>
        /// Steps that run (or would run).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecoverySteps, Reason = "One per recovery path step")]
        public List<BlackBoxRecoveryStep> Plan
        {
            get => _Plan;
            set => _Plan = value ?? new List<BlackBoxRecoveryStep>();
        }

        /// <summary>
        /// Per-step results (real run only).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecoverySteps, Reason = "One per recovery path step")]
        public List<BlackBoxRecoveryStepResult> StepResults
        {
            get => _StepResults;
            set => _StepResults = value ?? new List<BlackBoxRecoveryStepResult>();
        }

        /// <summary>
        /// Whether every step succeeded (true for a dry run that validated).
        /// </summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>
        /// Summary message.
        /// </summary>
        public string? Message { get; set; } = null;

        private List<BlackBoxRecoveryStep> _Plan = new List<BlackBoxRecoveryStep>();
        private List<BlackBoxRecoveryStepResult> _StepResults = new List<BlackBoxRecoveryStepResult>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryRunResult()
        {
        }
    }
}
