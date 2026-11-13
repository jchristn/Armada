namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Resilience analysis of a black box failure (analysis.resilience): could Armada's handoff and landing have healed
    /// it by itself, why Armada did not catch it earlier, where in the Armada source it lives, and the Armada changes that
    /// would prevent it, as mission briefs. Filled by the advisor for every entry; never empty in handoff and landing scope.
    /// </summary>
    public class BlackBoxResilience
    {
        /// <summary>
        /// Whether the failure is in Armada's handoff and landing scope (phases Handoff, Landing, MergeQueue, Recovery and
        /// Provision, Armada handoff mechanics in Completion, and dock, worktree, branch or target-sync facts of any phase).
        /// </summary>
        public bool InScope { get; set; } = false;

        /// <summary>
        /// Why the failure is in scope (for example "phase Landing" or "the dock worktree was missing"), or null.
        /// </summary>
        public string? ScopeReason { get; set; } = null;

        /// <summary>
        /// Whether Armada could have recovered automatically.
        /// </summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>
        /// What Armada could have done automatically (retry with backoff, recreate the worktree from the branch, rebase onto
        /// the moved target, ...), or null.
        /// </summary>
        public string? SelfHealingProposal { get; set; } = null;

        /// <summary>
        /// Kind of detection gap.
        /// </summary>
        public BlackBoxDetectionGapEnum DetectionGapKind { get; set; } = BlackBoxDetectionGapEnum.Unknown;

        /// <summary>
        /// Why Armada did not catch or prevent it earlier, or null.
        /// </summary>
        public string? DetectionGap { get; set; } = null;

        /// <summary>
        /// The invariant that should hold so the failure cannot happen, or null.
        /// </summary>
        public string? Invariant { get; set; } = null;

        /// <summary>
        /// Armada source locations most likely involved (the failure site first, then the handoff and landing services),
        /// as repository-relative file:method.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxCodePointers, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> CodePointers
        {
            get => _CodePointers;
            set => _CodePointers = value ?? new List<string>();
        }

        /// <summary>
        /// Armada version that failed (read the code pointers at this build).
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Armada build commit that failed, or null.
        /// </summary>
        public string? ArmadaCommit { get; set; } = null;

        /// <summary>
        /// Proposed Armada changes as mission briefs, most impactful first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxProposedChanges, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxProposedChange> ProposedChanges
        {
            get => _ProposedChanges;
            set => _ProposedChanges = value ?? new List<BlackBoxProposedChange>();
        }

        /// <summary>
        /// The test that would have caught it, or null.
        /// </summary>
        public BlackBoxRegressionTest? RegressionTest { get; set; } = null;

        /// <summary>
        /// Overall severity: the highest proposed change's, escalated by recurrence (1 occurrence Medium, 3 or more High,
        /// 5 or more or seen in 2 or more Armada versions Critical).
        /// </summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Low;

        /// <summary>
        /// Entries with the same fingerprint in the last 30 days (this one included).
        /// </summary>
        public int Occurrences30Days { get; set; } = 0;

        /// <summary>
        /// Armada versions the fingerprint was seen in.
        /// </summary>
        public int ArmadaVersionCount { get; set; } = 0;

        /// <summary>
        /// Automatic recoveries Armada already attempted before this failure and their outcome (from the handoff trail).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxAttemptedRecoveries, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> AttemptedRecoveries
        {
            get => _AttemptedRecoveries;
            set => _AttemptedRecoveries = value ?? new List<string>();
        }

        /// <summary>
        /// Why no Armada change is suggested (outside the handoff and landing scope), or null.
        /// </summary>
        public string? NoChangeReason { get; set; } = null;

        /// <summary>
        /// Rules that contributed (BBX-PRIOR-ARMADA when the scope prior applied).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxResilienceRules, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> RuleIds
        {
            get => _RuleIds;
            set => _RuleIds = value ?? new List<string>();
        }

        private List<string> _CodePointers = new List<string>();
        private List<BlackBoxProposedChange> _ProposedChanges = new List<BlackBoxProposedChange>();
        private List<string> _AttemptedRecoveries = new List<string>();
        private List<string> _RuleIds = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxResilience()
        {
        }
    }
}
