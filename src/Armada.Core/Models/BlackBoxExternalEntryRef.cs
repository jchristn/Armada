namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A black box entry whose objects live outside the database (listed in backup manifests and restore reports, since
    /// a database backup does not contain them).
    /// </summary>
    public class BlackBoxExternalEntryRef
    {
        /// <summary>
        /// Entry id.
        /// </summary>
        public string EntryId { get; set; } = String.Empty;

        /// <summary>
        /// Storage target.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Unknown;

        /// <summary>
        /// Key prefix of the entry's objects in the target.
        /// </summary>
        public string? StoragePrefix { get; set; } = null;

        /// <summary>
        /// Objects in the entry.
        /// </summary>
        public int ObjectCount { get; set; } = 0;

        /// <summary>
        /// Stored bytes.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxExternalEntryRef()
        {
        }
    }
}
