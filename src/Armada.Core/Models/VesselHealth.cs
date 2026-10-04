namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The current health row for one vessel (at most one per vessel). Every column the health table
    /// sorts or filters on is typed and indexed; lists live in child tables (<see cref="VesselHealthFinding"/>,
    /// <see cref="VesselDependency"/>, <see cref="VesselHealthOverride"/>).
    /// Status semantics: the status columns (<see cref="OverallStatus"/> and every per-criterion status) hold EFFECTIVE
    /// values, that is, after manual overrides have been applied. The health evaluator writes them; the persistence
    /// layer stores them as given. Raw (pre-override) results are kept on the findings.
    /// When returned from enumeration, a vessel that has never been evaluated still appears, with a null
    /// <see cref="Id"/>, Unknown statuses, and null measurements.
    /// </summary>
    public class VesselHealth
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (vhl_ prefix). Null for a vessel that has not been evaluated yet (enumeration only); an upsert backfills an empty value.
        /// </summary>
        public string? Id
        {
            get => _Id;
            set => _Id = value;
        }

        /// <summary>
        /// Owning tenant identifier. Every row carries a tenant; reads and enumerations are scoped by it.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Vessel identifier. Unique: one row per vessel. Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Worst status among the scored criteria. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum OverallStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Timestamp in UTC of the last evaluation, or null.
        /// </summary>
        public DateTime? EvaluatedUtc { get; set; } = null;

        /// <summary>
        /// Duration of the last evaluation in milliseconds. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public long? EvaluationDurationMs
        {
            get => _EvaluationDurationMs;
            set => _EvaluationDurationMs = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Stable error code when the evaluation itself failed, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Path that was evaluated (working directory or bare clone), or null.
        /// </summary>
        public string? EvaluatedPath { get; set; } = null;

        /// <summary>
        /// Checked-out branch name, or null.
        /// </summary>
        public string? CurrentBranch { get; set; } = null;

        /// <summary>
        /// Whether tracked files are modified, or null when unknown or not applicable.
        /// </summary>
        public bool? IsDirty { get; set; } = null;

        /// <summary>
        /// Number of untracked files. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? UntrackedCount
        {
            get => _UntrackedCount;
            set => _UntrackedCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Commits ahead of the default branch. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? AheadOfDefault
        {
            get => _AheadOfDefault;
            set => _AheadOfDefault = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Commits behind the default branch. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? BehindDefault
        {
            get => _BehindDefault;
            set => _BehindDefault = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Commits ahead of the upstream branch. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? AheadOfUpstream
        {
            get => _AheadOfUpstream;
            set => _AheadOfUpstream = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Commits behind the upstream branch. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? BehindUpstream
        {
            get => _BehindUpstream;
            set => _BehindUpstream = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Timestamp in UTC of the most recent commit, or null.
        /// </summary>
        public DateTime? LastCommitUtc { get; set; } = null;

        /// <summary>
        /// Number of local branches. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? BranchCount
        {
            get => _BranchCount;
            set => _BranchCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of stale local branches. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? StaleBranchCount
        {
            get => _StaleBranchCount;
            set => _StaleBranchCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of leftover armada/* branches. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? ArmadaBranchCount
        {
            get => _ArmadaBranchCount;
            set => _ArmadaBranchCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Primary language or ecosystem detected, or null.
        /// </summary>
        public string? PrimaryLanguage { get; set; } = null;

        /// <summary>
        /// Number of projects detected. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? ProjectCount
        {
            get => _ProjectCount;
            set => _ProjectCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of outdated dependencies. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? OutdatedCount
        {
            get => _OutdatedCount;
            set => _OutdatedCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of dependencies with major version drift. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? OutdatedMajorCount
        {
            get => _OutdatedMajorCount;
            set => _OutdatedMajorCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of dependencies with known vulnerabilities. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? VulnerableCount
        {
            get => _VulnerableCount;
            set => _VulnerableCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Highest vulnerability severity across dependencies. Defaults to None.
        /// </summary>
        public VulnerabilitySeverityEnum MaxVulnerabilitySeverity { get; set; } = VulnerabilitySeverityEnum.None;

        /// <summary>
        /// Dependencies criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum DependencyStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Vulnerabilities criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum VulnerabilityStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Test infrastructure criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum TestInfraStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Continuous integration criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum CiStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Git divergence criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum DivergenceStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Working tree criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum WorkingTreeStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Branches criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum BranchStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Armada readiness criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum ReadinessStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Mission outcomes criterion status. Effective value (after manual overrides). Defaults to Unknown.
        /// </summary>
        public VesselHealthStatusEnum MissionOutcomeStatus { get; set; } = VesselHealthStatusEnum.Unknown;

        /// <summary>
        /// Status of the latest check run for the vessel, or null.
        /// </summary>
        public string? LastCheckRunStatus { get; set; } = null;

        /// <summary>
        /// Whether continuous integration configuration is present, or null when unknown.
        /// </summary>
        public bool? HasCiConfig { get; set; } = null;

        /// <summary>
        /// Whether a license file is present, or null when unknown.
        /// </summary>
        public bool? HasLicense { get; set; } = null;

        /// <summary>
        /// Whether a readme file is present, or null when unknown.
        /// </summary>
        public bool? HasReadme { get; set; } = null;

        /// <summary>
        /// Number of Armada readiness errors. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? ReadinessErrorCount
        {
            get => _ReadinessErrorCount;
            set => _ReadinessErrorCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of failed or landing-failed missions within the configured window. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public int? RecentMissionFailureCount
        {
            get => _RecentMissionFailureCount;
            set => _RecentMissionFailureCount = value.HasValue && value.Value < 0 ? 0 : value;
        }

        /// <summary>
        /// Hash of all manifest and lock files at the last dependency evaluation, or null.
        /// </summary>
        public string? ManifestHash { get; set; } = null;

        /// <summary>
        /// Timestamp in UTC of the last dependency evaluation, or null.
        /// </summary>
        public DateTime? DependenciesEvaluatedUtc { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Vessel name. Not persisted on this row: populated by joins on read and enumerate.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Vessel fleet identifier. Not persisted on this row: populated by joins on read and enumerate.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel fleet name. Not persisted on this row: populated by joins on read and enumerate.
        /// </summary>
        public string? FleetName { get; set; } = null;

        #endregion

        #region Private-Members

        private string? _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselHealthIdPrefix, 24);
        private string _VesselId = String.Empty;
        private long? _EvaluationDurationMs = null;
        private int? _UntrackedCount = null;
        private int? _AheadOfDefault = null;
        private int? _BehindDefault = null;
        private int? _AheadOfUpstream = null;
        private int? _BehindUpstream = null;
        private int? _BranchCount = null;
        private int? _StaleBranchCount = null;
        private int? _ArmadaBranchCount = null;
        private int? _ProjectCount = null;
        private int? _OutdatedCount = null;
        private int? _OutdatedMajorCount = null;
        private int? _VulnerableCount = null;
        private int? _ReadinessErrorCount = null;
        private int? _RecentMissionFailureCount = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealth()
        {
        }

        #endregion
    }
}
