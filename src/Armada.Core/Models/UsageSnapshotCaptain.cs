namespace Armada.Core.Models
{
    using System;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Link between one usage snapshot and one captain that was a member of the captured account group at
    /// capture time. Links are written with the snapshot in one transaction and are preserved as history
    /// even after a captain is deleted (a deleted captain keeps a safe, labeled link rather than rewriting
    /// history to a current account). Retention removes links with their snapshot.
    /// </summary>
    public class UsageSnapshotCaptain
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (usl_ prefix).
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
        /// The snapshot this link belongs to.
        /// </summary>
        public string SnapshotId
        {
            get => _SnapshotId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(SnapshotId));
                _SnapshotId = value;
            }
        }

        /// <summary>
        /// The captain that was a member of the account group at capture time.
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
        /// The snapshot's observation time, copied onto the link so member-by-time lookups (mission
        /// correlation, retention) never join back to the snapshot.
        /// </summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UsageSnapshotCaptainIdPrefix, 24);
        private string _TenantId = String.Empty;
        private string _SnapshotId = String.Empty;
        private string _CaptainId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageSnapshotCaptain()
        {
        }

        /// <summary>
        /// Instantiate with tenant, snapshot, captain and observation time.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="snapshotId">Snapshot identifier.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <param name="capturedUtc">Snapshot observation time (UTC).</param>
        public UsageSnapshotCaptain(string tenantId, string snapshotId, string captainId, DateTime capturedUtc)
        {
            TenantId = tenantId;
            SnapshotId = snapshotId;
            CaptainId = captainId;
            CapturedUtc = capturedUtc;
        }

        #endregion
    }
}