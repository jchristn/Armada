namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A keyset scan over black box entries in id order (storage migration, the missing-object check, expiry recompute and
    /// backup manifests). Entries in the Deleting state are skipped.
    /// </summary>
    public class BlackBoxScanQuery
    {
        /// <summary>
        /// Return entries whose id sorts after this one, or null to start at the beginning.
        /// </summary>
        public string? AfterId { get; set; } = null;

        /// <summary>
        /// Storage target filter, or null for every target.
        /// </summary>
        public BlackBoxStorageTargetEnum? StorageTarget { get; set; } = null;

        /// <summary>
        /// When true, StorageTarget is excluded instead of required.
        /// </summary>
        public bool ExcludeStorageTarget { get; set; } = false;

        /// <summary>
        /// Storage migration filter: entries not yet fully on this target (their storage_target differs, or, for an
        /// external target, one of their objects still lives in the database). Combined with StorageTarget when both are set.
        /// </summary>
        public BlackBoxStorageTargetEnum? NeedsMigrationTo { get; set; } = null;

        /// <summary>
        /// Only entries of this vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Rows per page (1 to 1000).
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = Math.Clamp(value, 1, 1000);
        }

        private int _PageSize = 100;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxScanQuery()
        {
        }
    }
}
