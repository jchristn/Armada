namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request body for batch purging merge queue entries.
    /// </summary>
    public class PurgeMergeEntriesRequest
    {
        #region Public-Members

        /// <summary>
        /// List of merge entry IDs to purge (mrg_ prefix).
        /// </summary>
        public List<string> EntryIds { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public PurgeMergeEntriesRequest()
        {
        }

        #endregion
    }
}
