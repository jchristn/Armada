namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a batch delete operation.
    /// </summary>
    public class DeleteMultipleResult
    {
        #region Public-Members

        /// <summary>
        /// Operation status.
        /// </summary>
        public string Status { get; set; } = "deleted";

        /// <summary>
        /// Number of entities successfully deleted.
        /// </summary>
        public int Deleted { get; set; }

        /// <summary>
        /// Entities that were skipped (not found, actively in use, etc.).
        /// </summary>
        public List<DeleteMultipleSkipped> Skipped { get; set; } = new List<DeleteMultipleSkipped>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public DeleteMultipleResult()
        {
        }

        #endregion
    }
}
