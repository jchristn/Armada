namespace Armada.Core.Models
{
    /// <summary>
    /// A merge queue entry that was skipped during a batch purge operation.
    /// </summary>
    public class MergeQueuePurgeSkipped
    {
        #region Public-Members

        /// <summary>
        /// Merge entry ID that was skipped.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Reason the entry was skipped.
        /// </summary>
        public string Reason { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public MergeQueuePurgeSkipped()
        {
        }

        /// <summary>
        /// Instantiate with entry ID and reason.
        /// </summary>
        /// <param name="entryId">Merge entry ID.</param>
        /// <param name="reason">Reason for skipping.</param>
        public MergeQueuePurgeSkipped(string entryId, string reason)
        {
            EntryId = entryId ?? "";
            Reason = reason ?? "";
        }

        #endregion
    }
}
