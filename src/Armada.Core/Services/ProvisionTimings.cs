namespace Armada.Core.Services
{
    using System;
    using System.Globalization;

    /// <summary>Per-phase durations of one local dock provisioning, logged at Debug to locate per-repository contention.</summary>
    internal sealed class ProvisionTimings
    {
        internal long LockWaitMs { get; set; }
        internal long RepositoryCheckMs { get; set; }
        internal long FetchMs { get; set; }
        internal long PruneMs { get; set; }
        internal long CleanupMs { get; set; }
        internal long WorktreeMs { get; set; }
        internal long RecordMs { get; set; }

        /// <summary>Compact one-line form for the log.</summary>
        /// <returns>Text.</returns>
        public override string ToString()
            => String.Format(CultureInfo.InvariantCulture, "lock-wait {0}, repo-check {1}, fetch {2}, prune {3}, cleanup {4}, worktree {5}, record {6}",
                LockWaitMs, RepositoryCheckMs, FetchMs, PruneMs, CleanupMs, WorktreeMs, RecordMs);
    }
}
