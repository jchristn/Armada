namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A black box entry: the index row (blackbox_entries) for one captured mission or voyage failure. The index is always in the database whatever the storage target; only object bytes move between targets. Record is populated only by reads that ask for it.
    /// </summary>
    public class BlackBoxEntry
    {
        /// <summary>
        /// Unique identifier (bbx_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner of the subject.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Mission or voyage.
        /// </summary>
        public BlackBoxSubjectTypeEnum SubjectType { get; set; } = BlackBoxSubjectTypeEnum.Mission;

        /// <summary>
        /// Mission, or null for a voyage entry.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Fleet, or null.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Harbor, or null.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Dock, or null.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Merge-queue entry, or null.
        /// </summary>
        public string? MergeEntryId { get; set; } = null;

        /// <summary>
        /// Incident, or null.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Stage outcome, or null.
        /// </summary>
        public string? StageOutcomeId { get; set; } = null;

        /// <summary>
        /// Parent mission, or null.
        /// </summary>
        public string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// Denormalized mission (or voyage) title, kept after the mission is purged.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Denormalized vessel name.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Attempt number for the subject, starting at 1.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Whether this is the subject's current entry (one per subject).
        /// </summary>
        public bool IsCurrent { get; set; } = true;

        /// <summary>
        /// The previous entry for the same subject, or null.
        /// </summary>
        public string? SupersedesId { get; set; } = null;

        /// <summary>
        /// Phase.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Failure kind, or null.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Status before the failure write.
        /// </summary>
        public string? StatusBefore { get; set; } = null;

        /// <summary>
        /// Status after the failure write.
        /// </summary>
        public string? StatusAfter { get; set; } = null;

        /// <summary>
        /// Triage verdict.
        /// </summary>
        public BlackBoxTriageEnum Triage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Failure-mode fingerprint (64 hex characters), or null.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// How it was captured.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// Processing state.
        /// </summary>
        public BlackBoxStateEnum State { get; set; } = BlackBoxStateEnum.Captured;

        /// <summary>
        /// Resolution.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Open;

        /// <summary>
        /// What fixed it (at most 2000 characters).
        /// </summary>
        public string? ResolutionNote { get; set; } = null;

        /// <summary>
        /// Link to what fixed it: mission id, commit, PR or issue URL (at most 1000 characters).
        /// </summary>
        public string? ResolutionLink { get; set; } = null;

        /// <summary>
        /// Pinned entries are never expired or evicted.
        /// </summary>
        public bool Pinned { get; set; } = false;

        /// <summary>
        /// Where the entry's object bytes are stored.
        /// </summary>
        public BlackBoxStorageTargetEnum StorageTarget { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Object key prefix for external targets, or null.
        /// </summary>
        public string? StoragePrefix { get; set; } = null;

        /// <summary>
        /// Stored (compressed) size of the entry's objects.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Number of objects.
        /// </summary>
        public int ObjectCount { get; set; } = 0;

        /// <summary>
        /// Record schema version.
        /// </summary>
        public int SchemaVersion { get; set; } = BlackBoxLimits.SchemaVersion;

        /// <summary>
        /// Advisor rules version.
        /// </summary>
        public int RulesVersion { get; set; } = 0;

        /// <summary>
        /// Armada version that captured it.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Armada commit that captured it, or null.
        /// </summary>
        public string? ArmadaCommit { get; set; } = null;

        /// <summary>
        /// Redacted reason excerpt for lists and search (at most 1000 characters).
        /// </summary>
        public string? ReasonExcerpt { get; set; } = null;

        /// <summary>
        /// Analysis summary excerpt for lists and search (at most 2000 characters), or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Enrichment attempts so far.
        /// </summary>
        public int EnrichmentAttempts { get; set; } = 0;

        /// <summary>
        /// Last enrichment or storage error (redacted), or null.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// When the sweeper may retry enrichment (UTC), or null.
        /// </summary>
        public DateTime? NextAttemptUtc { get; set; } = null;

        /// <summary>
        /// When the entry expires (UTC); null when pinned or kept forever.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Enriched (UTC), or null.
        /// </summary>
        public DateTime? EnrichedUtc { get; set; } = null;

        /// <summary>
        /// Analyzed (UTC), or null.
        /// </summary>
        public DateTime? AnalyzedUtc { get; set; } = null;

        /// <summary>
        /// Resolved (UTC), or null.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The record (record.json), populated only when a read asks for it; never stored in the row.
        /// </summary>
        public BlackBoxRecord? Record { get; set; } = null;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxEntryIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEntry()
        {
        }
    }
}
