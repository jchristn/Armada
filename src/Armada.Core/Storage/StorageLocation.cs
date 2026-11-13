namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Storage Location measurement contract.
    /// </summary>
    public class StorageLocation
    {
        /// <summary>Stable storage category.</summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>Normalized path, never file contents.</summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>Associated vessel or dock identifier.</summary>
        public string? EntityId { get; set; } = null;

        /// <summary>Known dock activity state, otherwise null.</summary>
        public string? Activity { get; set; } = null;

        /// <summary>Observed logical file bytes, null when unavailable.</summary>
        public long? Bytes { get; set; } = null;

        /// <summary>Number of measured files.</summary>
        public long FileCount { get; set; } = 0;

        /// <summary>Oldest measured file modification time.</summary>
        public DateTime? OldestWriteUtc { get; set; } = null;

        /// <summary>Newest measured file modification time.</summary>
        public DateTime? NewestWriteUtc { get; set; } = null;

        /// <summary>Complete, Partial, Missing, Unavailable or Skipped.</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>Bounded collection warnings.</summary>
        [CollectionBound(16, Reason = "At most one stop warning plus AddWarning's cap of 10 distinct codes; Receive rejects more than 10")]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Bounded largest-file detail.</summary>
        [CollectionBound(100, Reason = "StorageSettings.LargestFileLimit (1..100); Receive rejects more than 100")]
        public List<StorageFile> LargestFiles { get; set; } = new List<StorageFile>();

    }
}
