namespace Armada.Core.Models
{
    using System;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Effective-dated membership of a captain in one account group (tenant + host + runtime + account key).
    /// Snapshots and token records are attributed through these periods rather than a captain's current
    /// account, so a captain that changed credentials, hosts or providers keeps its history. A membership is
    /// open while <see cref="EffectiveToUtc"/> is null; it is closed on credential changes, host moves,
    /// provider identity changes or capture disable. Membership is never guessed or backfilled for legacy
    /// records. Captains are subscribers, never owners: ownership is the account on the host.
    /// </summary>
    public class UsageAccountMembership
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (usm_ prefix). Token records may reference it as their attribution period.
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
        /// The captain subscribed to the account group for this period.
        /// </summary>
        public string CaptainId
        {
            get => _CaptainId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(CaptainId));
                _CaptainId = value;
            }
        }

        /// <summary>
        /// Stable identity of the credential host (Admiral host id or Harbor id) the account lives on.
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
        /// Provider the account belongs to, when known. Descriptive only; not part of the group key.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque host-HMAC account key. Required: a capture without an identity never creates a membership
        /// (missing identity must not merge unrelated captains into an unknown group).
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
        /// Configuration revision of the probe configuration the membership was observed under, when the
        /// capture reports one (a Custom command fingerprint, for example). Provider or command identity
        /// changes invalidate membership before the next observation.
        /// </summary>
        public string? ConfigurationRevision { get; set; } = null;

        /// <summary>
        /// UTC time the membership opened. Observation windows that start before this belong to a previous
        /// membership, never this one.
        /// </summary>
        public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the membership closed, or null while it is open. A closed membership stays readable as
        /// attribution history.
        /// </summary>
        public DateTime? EffectiveToUtc { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UsageMembershipIdPrefix, 24);
        private string _TenantId = String.Empty;
        private string _CaptainId = String.Empty;
        private string _HostId = String.Empty;
        private string _AccountKey = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageAccountMembership()
        {
        }

        #endregion
    }
}