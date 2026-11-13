namespace Armada.Core.Models
{
    using System;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Hourly utilization rollup for one account group, window and model, keyed by tenant, host, runtime,
    /// account key, window name, model and UTC hour. Rollups are computed from raw snapshots with their
    /// weights (never averages of averages), are idempotent to re-run, and retain bounded history while raw
    /// rows expire. Every numeric field is null when no sample covered it: null means missing data, never
    /// zero. Status counts and coverage seconds describe how much of the hour the account was actually
    /// observed for.
    /// </summary>
    public class UsageHourlyRollup
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (ush_ prefix). The natural key (tenant, host, runtime, account key, window
        /// name, model, hour) is unique; the id is the stable row handle for paging.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier; every lookup is tenant-scoped.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// Stable identity of the credential host the account lives on.
        /// </summary>
        public string HostId
        {
            get => _HostId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(HostId));
                _HostId = value;
            }
        }

        /// <summary>
        /// Runtime the account is used through.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Provider the account belongs to, when known. Part of the rollup's natural key: rollups that
        /// differ only by provider are separate rows. Null means unknown and is stored as the empty string.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque host-HMAC account key of the rolled-up group.
        /// </summary>
        public string AccountKey
        {
            get => _AccountKey;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(AccountKey));
                _AccountKey = value;
            }
        }

        /// <summary>
        /// Window name the rollup aggregates (for example five_hour). Empty means the rollup aggregates
        /// capture-level samples with no window (status counts only).
        /// </summary>
        public string WindowName
        {
            get => _WindowName;
            set => _WindowName = value ?? String.Empty;
        }

        /// <summary>
        /// Model the window applies to, for per-model windows. Empty means account-wide.
        /// </summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? String.Empty;
        }

        /// <summary>
        /// Start of the rolled-up UTC hour.
        /// </summary>
        public DateTime HourUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of raw samples the rollup aggregated, including samples without percentages.
        /// </summary>
        public int SampleCount
        {
            get => _SampleCount;
            set => _SampleCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Utilization percent of the newest sample in the hour. Null when no sample reported a percentage.
        /// </summary>
        public double? LastPercent { get; set; } = null;

        /// <summary>
        /// Lowest utilization percent observed in the hour. Null when no sample reported a percentage.
        /// </summary>
        public double? MinPercent { get; set; } = null;

        /// <summary>
        /// Highest utilization percent observed in the hour. Null when no sample reported a percentage.
        /// </summary>
        public double? MaxPercent { get; set; } = null;

        /// <summary>
        /// Weighted mean utilization percent across the hour's samples. Null when no sample reported a
        /// percentage.
        /// </summary>
        public double? MeanPercent { get; set; } = null;

        /// <summary>
        /// Usage of the newest sample in the window's unit. Null when not reported.
        /// </summary>
        public long? LastUsed { get; set; } = null;

        /// <summary>
        /// Limit of the newest sample in the window's unit. Null when not reported.
        /// </summary>
        public long? LastLimit { get; set; } = null;

        /// <summary>
        /// Reset time of the newest sample in the hour. Null when not reported.
        /// </summary>
        public DateTime? LastResetUtc { get; set; } = null;

        /// <summary>
        /// Observation time of the first raw sample in the hour. Null when unknown.
        /// </summary>
        public DateTime? FirstObservedUtc { get; set; } = null;

        /// <summary>
        /// Observation time of the last raw sample in the hour. Null when unknown.
        /// </summary>
        public DateTime? LastObservedUtc { get; set; } = null;

        /// <summary>
        /// Number of Ok samples in the hour.
        /// </summary>
        public int OkCount
        {
            get => _OkCount;
            set => _OkCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of Unsupported samples in the hour.
        /// </summary>
        public int UnsupportedCount
        {
            get => _UnsupportedCount;
            set => _UnsupportedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of AuthMissing samples in the hour.
        /// </summary>
        public int AuthMissingCount
        {
            get => _AuthMissingCount;
            set => _AuthMissingCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of Failed samples in the hour.
        /// </summary>
        public int FailedCount
        {
            get => _FailedCount;
            set => _FailedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of Estimated samples in the hour.
        /// </summary>
        public int EstimatedCount
        {
            get => _EstimatedCount;
            set => _EstimatedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Seconds of the hour the account's utilization was actually covered by observations. Null when
        /// the rollup does not track coverage.
        /// </summary>
        public double? CoverageSeconds { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        /// <summary>Number of observations with a numeric percentage, used for exact weighted means.</summary>
        public int PercentSampleCount { get; set; }

        /// <summary>Status of the newest observation, independent of historical status counts.</summary>
        public string? LastStatus { get; set; }

        /// <summary>True when the contributing raw rows were deleted in the same transaction as this aggregate.</summary>
        public bool RawPruned { get; set; }

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UsageRollupIdPrefix, 24);
        private string _TenantId = String.Empty;
        private string _HostId = String.Empty;
        private string _AccountKey = String.Empty;
        private string _WindowName = String.Empty;
        private string _Model = String.Empty;
        private int _SampleCount = 0;
        private int _OkCount = 0;
        private int _UnsupportedCount = 0;
        private int _AuthMissingCount = 0;
        private int _FailedCount = 0;
        private int _EstimatedCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageHourlyRollup()
        {
        }

        #endregion
    }
}