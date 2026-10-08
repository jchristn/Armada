namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Jobs a Harbor finished and failed over a metrics window. Missions are counted apart from every other launch (Ask turns and captain chat, planning, refinement, context builds, and other launches), which the series calls interactive. A job counts in the bucket in which it ended: Succeeded and Stopped count as finished, Failed and Lost as failed.
    /// </summary>
    public class HarborJobMetrics
    {
        #region Public-Members

        /// <summary>
        /// One entry per bucket, oldest first.
        /// </summary>
        public List<HarborJobBucket> Buckets { get; set; } = new List<HarborJobBucket>();

        /// <summary>
        /// Missions finished in the window.
        /// </summary>
        public int MissionsFinished { get; set; } = 0;

        /// <summary>
        /// Missions failed in the window.
        /// </summary>
        public int MissionsFailed { get; set; } = 0;

        /// <summary>
        /// Interactive and other launches finished in the window.
        /// </summary>
        public int InteractiveFinished { get; set; } = 0;

        /// <summary>
        /// Interactive and other launches failed in the window.
        /// </summary>
        public int InteractiveFailed { get; set; } = 0;

        /// <summary>
        /// Jobs running on the Harbor now, as the Admiral recorded them.
        /// </summary>
        public int Running { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborJobMetrics()
        {
        }

        #endregion
    }
}
