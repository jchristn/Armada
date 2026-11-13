namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One failure mode (fingerprint) of a JSON maintainer bundle.
    /// </summary>
    public class BlackBoxBundleGroup
    {
        /// <summary>
        /// Fingerprint (the entry id when the entry has none).
        /// </summary>
        public string Fingerprint { get; set; } = "";

        /// <summary>
        /// Entries in the bundle with this fingerprint.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Oldest of them.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Newest of them.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Armada versions affected.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxBundleVersions, Reason = "Capped by BlackBoxService.GetBundleAsync")]
        public List<string> ArmadaVersions
        {
            get => _ArmadaVersions;
            set => _ArmadaVersions = value ?? new List<string>();
        }

        /// <summary>
        /// Triage of the newest entry.
        /// </summary>
        public BlackBoxTriageEnum Triage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Rule ids of the newest entry's findings.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxFindings, Reason = "One per finding")]
        public List<string> RuleIds
        {
            get => _RuleIds;
            set => _RuleIds = value ?? new List<string>();
        }

        /// <summary>
        /// Failure site of the newest entry.
        /// </summary>
        public BlackBoxSite? Site { get; set; } = null;

        /// <summary>
        /// Armada improvement hypotheses of the newest entry.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxImprovements, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxImprovement> Improvements
        {
            get => _Improvements;
            set => _Improvements = value ?? new List<BlackBoxImprovement>();
        }

        /// <summary>
        /// Resilience severity of the newest entry (escalated by recurrence).
        /// </summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Low;

        /// <summary>
        /// Whether Armada could have healed the newest entry by itself.
        /// </summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>
        /// Proposed Armada changes of the newest entry, as mission briefs.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxProposedChanges, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxProposedChange> ProposedChanges
        {
            get => _ProposedChanges;
            set => _ProposedChanges = value ?? new List<BlackBoxProposedChange>();
        }

        /// <summary>
        /// The regression test that would have caught the newest entry, or null.
        /// </summary>
        public BlackBoxRegressionTest? RegressionTest { get; set; } = null;

        /// <summary>
        /// The newest entry.
        /// </summary>
        public BlackBoxEntrySummary? Newest { get; set; } = null;

        /// <summary>
        /// The other entries with this fingerprint.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxBundleEntries, Reason = "A bundle holds at most 50 entries")]
        public List<BlackBoxEntrySummary> Others
        {
            get => _Others;
            set => _Others = value ?? new List<BlackBoxEntrySummary>();
        }

        private List<string> _ArmadaVersions = new List<string>();
        private List<string> _RuleIds = new List<string>();
        private List<BlackBoxImprovement> _Improvements = new List<BlackBoxImprovement>();
        private List<BlackBoxEntrySummary> _Others = new List<BlackBoxEntrySummary>();
        private List<BlackBoxProposedChange> _ProposedChanges = new List<BlackBoxProposedChange>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBundleGroup()
        {
        }
    }
}
