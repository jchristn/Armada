namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One Armada resilience gap: a failure mode (fingerprint) triaged ArmadaDefect in handoff and landing scope, with
    /// its recurrence and the newest entry's resilience analysis.
    /// </summary>
    public class BlackBoxResilienceGap
    {
        /// <summary>
        /// Failure-mode fingerprint.
        /// </summary>
        public string Fingerprint { get; set; } = "";

        /// <summary>
        /// Phase of the newest entry.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step of the newest entry.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Failure kind of the newest entry, or null.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Entries with this fingerprint that match the filter.
        /// </summary>
        public long Occurrences { get; set; } = 0;

        /// <summary>
        /// Open entries among them.
        /// </summary>
        public long OpenCount { get; set; } = 0;

        /// <summary>
        /// First seen (UTC).
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last seen (UTC).
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Newest entry.
        /// </summary>
        public string? LatestEntryId { get; set; } = null;

        /// <summary>
        /// Mission of the newest entry, or null.
        /// </summary>
        public string? LatestMissionId { get; set; } = null;

        /// <summary>
        /// Title of that mission, or null.
        /// </summary>
        public string? LatestMissionTitle { get; set; } = null;

        /// <summary>
        /// Vessel of the newest entry, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Armada versions the fingerprint was seen in.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRelatedVersions, Reason = "Capped by BlackBoxService.EnumerateResilienceGapsAsync")]
        public List<string> ArmadaVersions
        {
            get => _ArmadaVersions;
            set => _ArmadaVersions = value ?? new List<string>();
        }

        /// <summary>
        /// Severity of the newest entry's resilience section.
        /// </summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Medium;

        /// <summary>
        /// Whether Armada could have healed it by itself.
        /// </summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>
        /// Whether the ArmadaDefect verdict rests on the scope prior alone (suspected, not proven).
        /// </summary>
        public bool Suspected { get; set; } = false;

        /// <summary>
        /// The most impactful proposed Armada change, or null.
        /// </summary>
        public BlackBoxProposedChange? TopProposedChange { get; set; } = null;

        /// <summary>
        /// Why Armada did not catch it earlier, or null.
        /// </summary>
        public string? DetectionGap { get; set; } = null;

        /// <summary>
        /// Rules behind the newest entry's resilience section.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxResilienceRules, Reason = "Capped by BlackBoxService.EnumerateResilienceGapsAsync")]
        public List<string> RuleIds
        {
            get => _RuleIds;
            set => _RuleIds = value ?? new List<string>();
        }

        private List<string> _ArmadaVersions = new List<string>();
        private List<string> _RuleIds = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxResilienceGap()
        {
        }
    }
}
