namespace Armada.Core.Metrics
{
    using System;

    /// <summary>
    /// A stretch of time during which something (a job) was active.
    /// </summary>
    public class TimeInterval
    {
        #region Public-Members

        /// <summary>
        /// When it became active (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; }

        /// <summary>
        /// When it stopped being active (UTC), or null when it still is.
        /// </summary>
        public DateTime? EndUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TimeInterval()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="startUtc">When it became active (UTC).</param>
        /// <param name="endUtc">When it stopped being active (UTC), or null when it still is.</param>
        public TimeInterval(DateTime startUtc, DateTime? endUtc)
        {
            StartUtc = startUtc;
            EndUtc = endUtc;
        }

        #endregion
    }
}
