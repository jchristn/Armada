namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a batch merge queue purge operation.
    /// </summary>
    public class MergeQueuePurgeResult
    {
        #region Public-Members

        /// <summary>
        /// Operation status.
        /// </summary>
        public string Status { get; set; } = "purged";

        /// <summary>
        /// Number of entries successfully purged.
        /// </summary>
        public int EntriesPurged { get; set; }

        /// <summary>
        /// Entries that were skipped (not found or not in terminal state).
        /// </summary>
        public List<MergeQueuePurgeSkipped> Skipped { get; set; } = new List<MergeQueuePurgeSkipped>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MergeQueuePurgeResult()
        {
        }

        #endregion
    }
}
