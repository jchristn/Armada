namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// What a database backup holds of the black box: Database-target entries are inside the backup; external-target
    /// entries are only indexed there (their objects stay in the external store) and are listed here.
    /// </summary>
    public class BlackBoxBackupInventory
    {
        /// <summary>
        /// Maximum external entries listed.
        /// </summary>
        public const int MaxListedEntries = 10000;

        /// <summary>
        /// Entries whose objects are in the database (and so in the backup).
        /// </summary>
        public long DatabaseEntries { get; set; } = 0;

        /// <summary>
        /// Bytes of those entries.
        /// </summary>
        public long DatabaseBytes { get; set; } = 0;

        /// <summary>
        /// Entries whose objects are in an external store (not in the backup).
        /// </summary>
        public long ExternalEntries { get; set; } = 0;

        /// <summary>
        /// Entries and bytes per external target.
        /// </summary>
        [CollectionBound(7, Reason = "One per BlackBoxStorageTargetEnum value")]
        public List<BlackBoxStorageTargetUsage> ExternalTargets
        {
            get => _ExternalTargets;
            set => _ExternalTargets = value ?? new List<BlackBoxStorageTargetUsage>();
        }

        /// <summary>
        /// The external entries (at most <see cref="MaxListedEntries"/>, in id order).
        /// </summary>
        [CollectionBound(MaxListedEntries, Reason = "Backup manifests list external entries up to a cap")]
        public List<BlackBoxExternalEntryRef> ExternalEntryRefs
        {
            get => _ExternalEntryRefs;
            set => _ExternalEntryRefs = value ?? new List<BlackBoxExternalEntryRef>();
        }

        /// <summary>
        /// True when more external entries exist than are listed.
        /// </summary>
        public bool ExternalEntryRefsTruncated { get; set; } = false;

        /// <summary>
        /// Plain-language summary.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        private List<BlackBoxStorageTargetUsage> _ExternalTargets = new List<BlackBoxStorageTargetUsage>();
        private List<BlackBoxExternalEntryRef> _ExternalEntryRefs = new List<BlackBoxExternalEntryRef>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBackupInventory()
        {
        }
    }
}
