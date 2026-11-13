namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// Query parameters for aggregated mission history.
    /// </summary>
    public class MissionHistoryQuery
    {
        #region Public-Members

        /// <summary>
        /// Inclusive start time in UTC. Default seven days ago.
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow.AddDays(-7);

        /// <summary>
        /// Exclusive end time in UTC. Default now.
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket size in minutes. Default 60; values below 1 are read as 60. The window divided by the bucket size may
        /// produce at most <see cref="CollectionLimits.MaxSummaryBuckets"/> buckets (see <see cref="Validate"/>).
        /// </summary>
        public int BucketMinutes { get; set; } = 60;

        /// <summary>
        /// Optional fleet filter (missions on the fleet's vessels).
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Optional vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Effective bucket size in minutes (60 when <see cref="BucketMinutes"/> is below 1).
        /// </summary>
        public int EffectiveBucketMinutes => BucketMinutes > 0 ? BucketMinutes : 60;

        /// <summary>
        /// <see cref="FromUtc"/> as UTC.
        /// </summary>
        public DateTime FromUtcNormalized => FromUtc.Kind == DateTimeKind.Utc ? FromUtc : FromUtc.ToUniversalTime();

        /// <summary>
        /// <see cref="ToUtc"/> as UTC.
        /// </summary>
        public DateTime ToUtcNormalized => ToUtc.Kind == DateTimeKind.Utc ? ToUtc : ToUtc.ToUniversalTime();

        /// <summary>
        /// Start of the first bucket: <see cref="FromUtc"/> rounded down to a multiple of the bucket size.
        /// </summary>
        public DateTime AlignedStartUtc
        {
            get
            {
                long bucketTicks = TimeSpan.FromMinutes(EffectiveBucketMinutes).Ticks;
                return new DateTime((FromUtcNormalized.Ticks / bucketTicks) * bucketTicks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// <see cref="AlignedStartUtc"/> in whole seconds since the Unix epoch (what the database providers subtract to
        /// compute a mission's bucket index).
        /// </summary>
        public long AlignedStartEpochSeconds => (AlignedStartUtc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerSecond;

        /// <summary>
        /// Bucket size in seconds.
        /// </summary>
        public long BucketSeconds => (long)EffectiveBucketMinutes * 60L;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reject a window and bucket size that would produce more than <see cref="CollectionLimits.MaxSummaryBuckets"/>
        /// buckets.
        /// </summary>
        /// <exception cref="CollectionLimitExceededException">Thrown when the window would produce too many buckets.</exception>
        public void Validate()
        {
            CollectionLimits.RequireBuckets(ToUtcNormalized - AlignedStartUtc, TimeSpan.FromMinutes(EffectiveBucketMinutes));
        }

        #endregion
    }
}
