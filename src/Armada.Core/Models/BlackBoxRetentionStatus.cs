namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Retention status: the policy, usage, and the last and next garbage collection runs.
    /// </summary>
    public class BlackBoxRetentionStatus
    {
        /// <summary>
        /// Effective policy.
        /// </summary>
        public Armada.Core.Settings.BlackBoxRetentionSettings Policy { get; set; } = new Armada.Core.Settings.BlackBoxRetentionSettings();

        /// <summary>
        /// Entries in scope.
        /// </summary>
        public long TotalEntries { get; set; } = 0;

        /// <summary>
        /// Stored bytes in scope.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Pinned entries in scope.
        /// </summary>
        public long PinnedEntries { get; set; } = 0;

        /// <summary>
        /// Entries waiting for their delete to finish.
        /// </summary>
        public long DeletingEntries { get; set; } = 0;

        /// <summary>
        /// Entries already past expiry (deleted by the next run unless protected).
        /// </summary>
        public long ExpiredEntries { get; set; } = 0;

        /// <summary>
        /// Last garbage collection report, or null.
        /// </summary>
        public BlackBoxGcReport? LastRun { get; set; } = null;

        /// <summary>
        /// Next scheduled run (UTC), or null when not scheduled.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRetentionStatus()
        {
        }
    }
}
