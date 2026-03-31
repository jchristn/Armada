namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Generic request body for batch deleting multiple entities by ID.
    /// </summary>
    public class DeleteMultipleRequest
    {
        #region Public-Members

        /// <summary>
        /// List of entity IDs to delete.
        /// </summary>
        public List<string> Ids { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public DeleteMultipleRequest()
        {
        }

        #endregion
    }
}
