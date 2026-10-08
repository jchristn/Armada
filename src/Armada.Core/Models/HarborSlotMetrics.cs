namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Metrics;

    /// <summary>
    /// How many of a Harbor's job slots were in use over a metrics window. A job occupies a slot from when the Harbor started it (or from launch, until the Harbor reports the start) until it ends.
    /// </summary>
    public class HarborSlotMetrics
    {
        #region Public-Members

        /// <summary>
        /// The Harbor's capacity (its current MaxConcurrentJobs).
        /// </summary>
        public int MaxConcurrentJobs { get; set; } = 0;

        /// <summary>
        /// One entry per bucket, oldest first: the peak and the time-weighted average of concurrent jobs.
        /// </summary>
        public List<ConcurrencyBucket> Buckets { get; set; } = new List<ConcurrencyBucket>();

        /// <summary>
        /// The most jobs running at once anywhere in the window.
        /// </summary>
        public int Peak { get; set; } = 0;

        /// <summary>
        /// The time-weighted average of concurrent jobs over the window, rounded to two decimals.
        /// </summary>
        public double Average { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborSlotMetrics()
        {
        }

        #endregion
    }
}
