namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Jobs a Harbor finished and failed in one time bucket.
    /// </summary>
    public class HarborJobBucket
    {
        #region Public-Members

        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>
        /// Missions that finished (exit code 0, or stopped on request).
        /// </summary>
        public int MissionsFinished { get; set; } = 0;

        /// <summary>
        /// Missions that failed (non-zero exit, could not launch, or lost).
        /// </summary>
        public int MissionsFailed { get; set; } = 0;

        /// <summary>
        /// Other launches (Ask turns, chat, planning, refinement, context builds, other) that finished.
        /// </summary>
        public int InteractiveFinished { get; set; } = 0;

        /// <summary>
        /// Other launches that failed.
        /// </summary>
        public int InteractiveFailed { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborJobBucket()
        {
        }

        #endregion
    }
}
