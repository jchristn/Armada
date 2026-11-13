namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Generic request body for batch deleting multiple entities by ID.
    /// </summary>
    public class DeleteMultipleRequest
    {
        #region Public-Members

        /// <summary>
        /// List of entity IDs to delete. At most <see cref="CollectionLimits.MaxRequestItems"/>; call
        /// <see cref="RequireWithinLimit"/> before acting on the request.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
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

        #region Public-Methods

        /// <summary>
        /// Reject a request carrying more than <see cref="CollectionLimits.MaxRequestItems"/> ids. Bulk deletes call
        /// this (or <see cref="CollectionLimits.RequireRequestItems{T}"/> on their own id list) before deleting
        /// anything, which also bounds the derived <see cref="DeleteMultipleResult.Skipped"/> list.
        /// </summary>
        /// <exception cref="CollectionLimitExceededException">Thrown when Ids holds too many ids.</exception>
        public void RequireWithinLimit()
        {
            CollectionLimits.RequireRequestItems(Ids, nameof(Ids));
        }

        #endregion
    }
}
