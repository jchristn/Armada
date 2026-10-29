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
        /// Whether every step succeeded (true for a dry run that validated and whose preconditions are met).
        /// </summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>
        /// Whether the run was refused because a step's precondition is not met (for example restart_mission on a
        /// mission that is already Pending). A refused dry run issues no confirm token; a refused confirmed run runs
        /// nothing.
        /// </summary>
        public bool Refused { get; set; } = false;

        /// <summary>
        /// The unmet preconditions, one per step that cannot run; empty when every step can run.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRecoverySteps, Reason = "At most one per recovery path step")]
        public List<string> UnmetPreconditions
        {
            get => _UnmetPreconditions;
            set => _UnmetPreconditions = value ?? new List<string>();
        }

        /// <summary>
        /// Summary message.
        /// </summary>
        public string? Message { get; set; } = null;

        private List<BlackBoxRecoveryStep> _Plan = new List<BlackBoxRecoveryStep>();
        private List<BlackBoxRecoveryStepResult> _StepResults = new List<BlackBoxRecoveryStepResult>();
        private List<string> _UnmetPreconditions = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryRunResult()
        {
        }
    }
}
