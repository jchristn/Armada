namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of a batch delete call.
    /// </summary>
    public class BatchDeleteResult
    {
        #region Public-Members

        /// <summary>
        /// Number of records deleted.
        /// </summary>
        public int Deleted { get; set; } = 0;

        /// <summary>
        /// Ids that were not deleted. Never null.
        /// </summary>
        public List<BatchDeleteSkipped> Skipped { get; set; } = new List<BatchDeleteSkipped>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatchDeleteResult()
        {
        }

        #endregion
    }
}
