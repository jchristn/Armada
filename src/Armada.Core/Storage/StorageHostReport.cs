namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Storage HostReport measurement contract.
    /// </summary>
    public class StorageHostReport
    {
        /// <summary>Most volumes in one host report.</summary>
        public const int MaxVolumes = 256;

        /// <summary>Most warnings in one host report.</summary>
        public const int MaxWarnings = 20;

        /// <summary>Admiral or authenticated Harbor identifier.</summary>
        public string HostId { get; set; } = String.Empty;

        /// <summary>Host display name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Collection completion time in UTC.</summary>
        public DateTime MeasuredUtc { get; set; } = DateTime.MinValue;

        /// <summary>Source collection cadence in seconds, default 300.</summary>
        public int IntervalSeconds { get; set; } = 300;

        /// <summary>Whether measurements are old or the host is offline.</summary>
        public bool Stale { get; set; } = false;

        /// <summary>Complete, Partial, Pending or Unavailable.</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>Exclusive filesystem measurements.</summary>
        [CollectionBound(5000, Reason = "StorageSettings.MaxLocations (10..5000); the collector stops at it and Receive rejects larger Harbor reports")]
        public List<StorageLocation> Locations { get; set; } = new List<StorageLocation>();

        /// <summary>Distinct containing volumes.</summary>
        [CollectionBound(StorageHostReport.MaxVolumes, Reason = "One per distinct mounted volume; the collector and Receive enforce the limit")]
        public List<StorageVolume> Volumes { get; set; } = new List<StorageVolume>();

        /// <summary>Bounded host collection warnings.</summary>
        [CollectionBound(StorageHostReport.MaxWarnings, Reason = "Distinct warning codes; the collector and Receive enforce the limit")]
        public List<string> Warnings { get; set; } = new List<string>();

    }
}
