namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box retention, expiration and garbage collection policy (blackBox.retention). Values out of range are clamped.
    /// A vessel can override RetentionDays with Vessel.BlackBoxRetentionDays. Pinned entries never expire.
    /// </summary>
    public class BlackBoxRetentionSettings
    {
        #region Public-Members

        /// <summary>
        /// Age in days after which an unpinned entry expires. Default 180, clamped to 0..3650; 0 keeps entries forever.
        /// Longer than event retention on purpose: the entry outlives the events it summarizes.
        /// </summary>
        public int RetentionDays
        {
            get => _RetentionDays;
            set => _RetentionDays = Math.Clamp(value, 0, 3650);
        }

        /// <summary>
        /// Days after resolution after which a resolved entry expires, when sooner than RetentionDays. Default 30, clamped
        /// to 0..3650; 0 means resolution does not shorten retention.
        /// </summary>
        public int ResolvedRetentionDays
        {
            get => _ResolvedRetentionDays;
            set => _ResolvedRetentionDays = Math.Clamp(value, 0, 3650);
        }

        /// <summary>
        /// Keep the current entry of a mission (or voyage) that still exists, even past expiry. Default true.
        /// </summary>
        public bool KeepLatestPerMission { get; set; } = true;

        /// <summary>
        /// Most entries per vessel; above it the oldest unpinned, non-current entries are evicted. Default 1000, clamped to
        /// 10..100000.
        /// </summary>
        public int MaxEntriesPerVessel
        {
            get => _MaxEntriesPerVessel;
            set => _MaxEntriesPerVessel = Math.Clamp(value, 10, 100000);
        }

        /// <summary>
        /// Global cap on stored bytes; above it the oldest unpinned resolved entries go first, then the oldest unpinned.
        /// Default 1073741824 (1 GB), clamped to 16777216 (16 MB)..1099511627776 (1 TB).
        /// </summary>
        public long MaxTotalBytes
        {
            get => _MaxTotalBytes;
            set => _MaxTotalBytes = Math.Clamp(value, 16777216L, 1099511627776L);
        }

        /// <summary>
        /// When a mission is purged, delete its entries too. Default false (entries are kept with the denormalized title).
        /// </summary>
        public bool DeleteWithMission { get; set; } = false;

        /// <summary>
        /// When a vessel is deleted, delete its entries. Default true.
        /// </summary>
        public bool DeleteWithVessel { get; set; } = true;

        /// <summary>
        /// Minutes between garbage collection runs. Default 60, clamped to 5..1440.
        /// </summary>
        public int GcIntervalMinutes
        {
            get => _GcIntervalMinutes;
            set => _GcIntervalMinutes = Math.Clamp(value, 5, 1440);
        }

        /// <summary>
        /// Entries per garbage collection batch. Default 200, clamped to 10..1000.
        /// </summary>
        public int GcBatchSize
        {
            get => _GcBatchSize;
            set => _GcBatchSize = Math.Clamp(value, 10, 1000);
        }

        /// <summary>
        /// Minimum age in hours before an unreferenced external object is deleted by the orphan sweep. Default 24, clamped
        /// to 1..720.
        /// </summary>
        public int OrphanGraceHours
        {
            get => _OrphanGraceHours;
            set => _OrphanGraceHours = Math.Clamp(value, 1, 720);
        }

        #endregion

        #region Private-Members

        private int _RetentionDays = 180;
        private int _ResolvedRetentionDays = 30;
        private int _MaxEntriesPerVessel = 1000;
        private long _MaxTotalBytes = 1073741824L;
        private int _GcIntervalMinutes = 60;
        private int _GcBatchSize = 200;
        private int _OrphanGraceHours = 24;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxRetentionSettings()
        {
        }

        #endregion
    }
}
