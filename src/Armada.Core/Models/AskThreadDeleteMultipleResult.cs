namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of POST /api/v1/ask/threads/delete/multiple: the IDs that were deleted and the IDs that were skipped,
    /// each skipped entry with the reason it was not deleted. Threads that belong to another user are reported as
    /// skipped ("Not found") exactly like the single delete returns 404 for them.
    /// </summary>
    public class AskThreadDeleteMultipleResult
    {
        #region Public-Members

        /// <summary>
        /// Operation status: Deleted when every ID was deleted, PartiallyDeleted when some were skipped, NoneDeleted
        /// when nothing was deleted.
        /// </summary>
        public DeleteMultipleStatusEnum Status { get; set; } = DeleteMultipleStatusEnum.Deleted;

        /// <summary>
        /// Number of threads successfully deleted.
        /// </summary>
        public int Deleted { get; set; }

        /// <summary>
        /// IDs of the threads that were deleted, in request order.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One entry per id of the request, which is limited to MaxRequestItems.")]
        public List<string> DeletedIds { get; set; } = new List<string>();

        /// <summary>
        /// IDs that were skipped (missing, empty, or owned by another user) with the reason.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One entry per id of the request, which is limited to MaxRequestItems.")]
        public List<DeleteMultipleSkipped> Skipped { get; set; } = new List<DeleteMultipleSkipped>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public AskThreadDeleteMultipleResult()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the Status enum based on the deleted and skipped counts. Call this after the delete loop completes.
        /// </summary>
        public void ResolveStatus()
        {
            if (Deleted == 0)
                Status = DeleteMultipleStatusEnum.NoneDeleted;
            else if (Skipped.Count > 0)
                Status = DeleteMultipleStatusEnum.PartiallyDeleted;
            else
                Status = DeleteMultipleStatusEnum.Deleted;
        }

        #endregion
    }
}
