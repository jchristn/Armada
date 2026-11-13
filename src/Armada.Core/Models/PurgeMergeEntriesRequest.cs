namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Request body for batch purging merge queue entries.
    /// </summary>
    public class PurgeMergeEntriesRequest
    {
        #region Public-Members

        /// <summary>
        /// List of merge entry IDs to purge (mrg_ prefix).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
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
