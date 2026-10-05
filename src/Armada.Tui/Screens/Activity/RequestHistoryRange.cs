namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// An activity chart range on the API Requests screen (the dashboard's Last Hour, Last Day, Last Week, Last Month):
    /// bucket size, bucket count, and the aligned window ending at the current bucket.
    /// </summary>
    public class RequestHistoryRange
    {
        #region Public-Members

        /// <summary>
        /// Id ("lastHour", "lastDay", "lastWeek", "lastMonth").
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Bucket size in minutes.
        /// </summary>
        public int BucketMinutes { get; }

        /// <summary>
        /// Number of buckets.
        /// </summary>
        public int SliceCount { get; }

        /// <summary>
        /// The four ranges in display order.
        /// </summary>
        public static IReadOnlyList<RequestHistoryRange> All { get; } = new List<RequestHistoryRange>
        {
            new RequestHistoryRange("lastHour", "Last Hour", 1, 60),
            new RequestHistoryRange("lastDay", "Last Day", 15, 96),
            new RequestHistoryRange("lastWeek", "Last Week", 120, 84),
            new RequestHistoryRange("lastMonth", "Last Month", 720, 60),
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="label">English label.</param>
        /// <param name="bucketMinutes">Bucket minutes.</param>
        /// <param name="sliceCount">Bucket count.</param>
        public RequestHistoryRange(string id, string label, int bucketMinutes, int sliceCount)
        {
            Id = id ?? "lastDay";
            Label = label ?? "";
            BucketMinutes = Math.Max(1, bucketMinutes);
            SliceCount = Math.Max(1, sliceCount);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find a range by id (Last Day when unknown).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <returns>Range.</returns>
        public static RequestHistoryRange Find(string? id)
        {
            return All.FirstOrDefault(r => r.Id == id) ?? All[1];
        }

        /// <summary>
        /// First bucket start (UTC) of the window ending with the bucket that contains <paramref name="nowUtc"/>.
        /// </summary>
        /// <param name="nowUtc">Now (UTC).</param>
        /// <returns>Window start.</returns>
        public DateTime StartUtc(DateTime nowUtc)
        {
            long bucketTicks = TimeSpan.FromMinutes(BucketMinutes).Ticks;
            long endExclusive = (nowUtc.Ticks / bucketTicks) * bucketTicks + bucketTicks;
            return new DateTime(endExclusive - SliceCount * bucketTicks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Last instant (UTC) of the window.
        /// </summary>
        /// <param name="nowUtc">Now (UTC).</param>
        /// <returns>Window end.</returns>
        public DateTime EndUtc(DateTime nowUtc)
        {
            long bucketTicks = TimeSpan.FromMinutes(BucketMinutes).Ticks;
            long endExclusive = (nowUtc.Ticks / bucketTicks) * bucketTicks + bucketTicks;
            return new DateTime(endExclusive - TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        }

        #endregion
    }
}
