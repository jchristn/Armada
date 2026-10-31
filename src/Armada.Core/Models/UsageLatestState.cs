namespace Armada.Core.Models
{
    using System;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Bounded per-account latest state: one row per account group (tenant + host + runtime + account key)
    /// holding the last good snapshot summary and, separately, the latest error. Keeping the error
    /// separately from the last good state means a failed capture never overwrites the last good windows,
    /// and an idle account keeps its real observation age instead of looking newly captured. The row
    /// carries no excerpt. When the account's membership closes, the state is expired (marked with
    /// <see cref="ExpiredUtc"/>) rather than refreshed, so it stops masquerading as live data.
    /// </summary>
    public class UsageLatestState
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (ust_ prefix).
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
        /// Provider the account belongs to, when known. Descriptive only.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque host-HMAC account key of the group.
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
        /// Identifier of the newest snapshot with usable data (Ok or Estimated), when one exists.
        /// </summary>
        public string? LastGoodSnapshotId { get; set; } = null;

        /// <summary>
        /// Observation time of the last good snapshot. Null when no good snapshot exists.
        /// </summary>
        public DateTime? LastGoodCapturedUtc { get; set; } = null;

        /// <summary>
        /// Time the Admiral committed the last good snapshot. Null when no good snapshot exists.
        /// </summary>
        public DateTime? LastGoodReceivedUtc { get; set; } = null;

        /// <summary>
        /// Status of the last good snapshot (Ok or Estimated), when one exists.
        /// </summary>
        public UsageSnapshotStatusEnum? LastGoodStatus { get; set; } = null;

        /// <summary>
        /// Serialized <see cref="UsageWindow"/> summary of the last good snapshot
        /// (usage_latest_state.last_good_windows_json). Raw column text; use <see cref="UsageWindowsJson"/>.
        /// Null when no good snapshot exists. Contains no excerpt.
        /// </summary>
        public string? LastGoodWindowsJson { get; set; } = null;

        /// <summary>
        /// Identifier of the newest failed, unsupported or auth-missing snapshot, when one exists. Stored
        /// separately so a failed capture never overwrites the last good state.
        /// </summary>
        public string? LastErrorSnapshotId { get; set; } = null;

        /// <summary>
        /// Observation time of the latest error snapshot. Null when no error snapshot exists.
        /// </summary>
        public DateTime? LastErrorCapturedUtc { get; set; } = null;

        /// <summary>
        /// Status of the latest error snapshot (Unsupported, AuthMissing or Failed), when one exists.
        /// </summary>
        public UsageSnapshotStatusEnum? LastErrorStatus { get; set; } = null;

        /// <summary>
        /// Stable error code of the latest error snapshot. Null when none exists.
        /// </summary>
        public string? LastErrorCode { get; set; } = null;

        /// <summary>
        /// UTC time the state was expired because the account's membership closed, when that happened. An
        /// expired state is retained read-only with its original observation age; it is never refreshed.
        /// </summary>
        public DateTime? ExpiredUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UsageLatestStateIdPrefix, 24);
        private string _TenantId = String.Empty;
        private string _HostId = String.Empty;
        private string _AccountKey = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageLatestState()
        {
        }

        #endregion
    }
}