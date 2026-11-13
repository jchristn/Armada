namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Storage Snapshot measurement contract. Host reports (the Admiral and one per Harbor) are not embedded; page them
    /// with GET /api/v1/storage/hosts. HostCount, FreshHostCount, ObservedFileBytes, and the latest history point
    /// summarize every host.
    /// </summary>
    public class StorageSnapshot
    {
        /// <summary>Last inventory completion, null before first sample.</summary>
        public DateTime? MeasuredUtc { get; set; } = null;

        /// <summary>Whether a background collection is in progress.</summary>
        public bool Collecting { get; set; } = false;

        /// <summary>Last collection failure, without secrets.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>Database allocation and records.</summary>
        public StorageDatabase Database { get; set; } = new StorageDatabase();

        /// <summary>Local and last-known remote host measurements. Internal: read through
        /// StorageInventoryService.EnumerateHosts; never serialized.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public List<StorageHostReport> Hosts { get; set; } = new List<StorageHostReport>();

        /// <summary>Number of hosts with a report: the Admiral plus one per registered Harbor.</summary>
        public int HostCount { get; set; } = 0;

        /// <summary>Number of hosts whose report is current (not stale) and Complete.</summary>
        public int FreshHostCount { get; set; } = 0;

        /// <summary>Observed filesystem bytes across every host's measured locations.</summary>
        public long ObservedFileBytes { get; set; } = 0;

        /// <summary>Bounded process-local history, reset on server restart (at most HistoryCapacity points, which is
        /// at most CollectionLimits.MaxSummaryBuckets).</summary>
        [CollectionBound(CollectionLimits.MaxSummaryBuckets, Reason = "Trimmed to StorageSettings.HistoryCapacity (2..1000) on every collection")]
        public List<StorageHistoryPoint> History { get; set; } = new List<StorageHistoryPoint>();

        /// <summary>Current retention settings in days, except build slots.</summary>
        [CollectionBound(16, Reason = "Fixed keys built in StorageInventoryService.Retention (10)")]
        public Dictionary<string, int> Retention { get; set; } = new Dictionary<string, int>();

        /// <summary>Collection interval in seconds.</summary>
        public int IntervalSeconds { get; set; } = 300;

        /// <summary>Maximum retained process-local samples.</summary>
        public int HistoryCapacity { get; set; } = 288;

    }
}
