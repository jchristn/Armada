namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A black box storage migration job: resumable, idempotent and paged.
    /// </summary>
    public class BlackBoxStorageMigrationJob
    {
        /// <summary>
        /// Unique identifier (bbm_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// State.
        /// </summary>
        public BlackBoxJobStateEnum State { get; set; } = BlackBoxJobStateEnum.Pending;

        /// <summary>
        /// Source filter, or null.
        /// </summary>
        public BlackBoxStorageTargetEnum? From { get; set; } = null;

        /// <summary>
        /// Target.
        /// </summary>
        public BlackBoxStorageTargetEnum To { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Whether the source copy is deleted.
        /// </summary>
        public bool DeleteSource { get; set; } = true;

        /// <summary>
        /// Entries per batch (1 to 1000).
        /// </summary>
        public int BatchSize
        {
            get => _BatchSize;
            set => _BatchSize = Math.Clamp(value, 1, 1000);
        }

        /// <summary>
        /// Entries to move.
        /// </summary>
        public long TotalEntries { get; set; } = 0;

        /// <summary>
        /// Entries moved.
        /// </summary>
        public long MigratedEntries { get; set; } = 0;

        /// <summary>
        /// Entries that failed.
        /// </summary>
        public long FailedEntries { get; set; } = 0;

        /// <summary>
        /// Bytes copied.
        /// </summary>
        public long BytesCopied { get; set; } = 0;

        /// <summary>
        /// Resume cursor: the last entry processed.
        /// </summary>
        public string? LastEntryId { get; set; } = null;

        /// <summary>
        /// Last error (no secrets), or null.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Started (UTC).
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Finished (UTC).
        /// </summary>
        public DateTime? FinishedUtc { get; set; } = null;

        /// <summary>
        /// Last progress update (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private int _BatchSize = 50;
        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxStorageMigrationIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStorageMigrationJob()
        {
        }
    }
}
