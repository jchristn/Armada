namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Number of missions of one status created in one history bucket, aggregated in the database.
    /// </summary>
    public class MissionHistoryBucketCount
    {
        #region Public-Members

        /// <summary>
        /// Zero-based bucket index from <see cref="MissionHistoryQuery.AlignedStartUtc"/>.
        /// </summary>
        public long BucketIndex { get; set; } = 0;

        /// <summary>
        /// Mission status.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Number of missions.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
