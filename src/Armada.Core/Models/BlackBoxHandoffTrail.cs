namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Handoff and landing trail of a mission failure (record.handoff): the ordered completion, stage check, handoff,
    /// landing attempt, retry, deferral and reconcile steps, the automatic recoveries Armada already attempted, the dock and
    /// lease transitions, and how far the target moved. Written by the Handoff enricher for entries in handoff and landing
    /// scope (also stored as evidence/handoff-timeline.json); null otherwise.
    /// </summary>
    public class BlackBoxHandoffTrail
    {
        /// <summary>
        /// Steps, oldest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxHandoffSteps, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxHandoffStep> Steps
        {
            get => _Steps;
            set => _Steps = value ?? new List<BlackBoxHandoffStep>();
        }

        /// <summary>
        /// Steps found (Steps keeps the newest 100).
        /// </summary>
        public int StepTotal { get; set; } = 0;

        /// <summary>
        /// Whether steps were cut.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Automatic recoveries attempted, one per kind.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxAutoRecoveries, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxAutoRecovery> AutoRecoveries
        {
            get => _AutoRecoveries;
            set => _AutoRecoveries = value ?? new List<BlackBoxAutoRecovery>();
        }

        /// <summary>
        /// Dock and lease transitions (provisioned, reclaimed, repaired, unstuck, preserved), oldest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDockTransitions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxHandoffStep> DockTransitions
        {
            get => _DockTransitions;
            set => _DockTransitions = value ?? new List<BlackBoxHandoffStep>();
        }

        /// <summary>
        /// Dock state when the failure was captured, or null.
        /// </summary>
        public string? DockStateAtFailure { get; set; } = null;

        /// <summary>
        /// Landing attempts seen (the first landing plus retries).
        /// </summary>
        public int LandingAttempts { get; set; } = 0;

        /// <summary>
        /// Target branch.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// Target tip when the mission started, or null.
        /// </summary>
        public string? TargetTipAtStart { get; set; } = null;

        /// <summary>
        /// Target tip at the failure, or null.
        /// </summary>
        public string? TargetTipAtFailure { get; set; } = null;

        /// <summary>
        /// Whether the target moved during the run (null when unknown).
        /// </summary>
        public bool? TargetMoved { get; set; } = null;

        /// <summary>
        /// Commits that landed on the target during the run, or null.
        /// </summary>
        public int? TargetCommitsDuringRun { get; set; } = null;

        /// <summary>
        /// Why parts are missing, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        private List<BlackBoxHandoffStep> _Steps = new List<BlackBoxHandoffStep>();
        private List<BlackBoxAutoRecovery> _AutoRecoveries = new List<BlackBoxAutoRecovery>();
        private List<BlackBoxHandoffStep> _DockTransitions = new List<BlackBoxHandoffStep>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxHandoffTrail()
        {
        }
    }
}
