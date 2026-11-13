namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a batch delete operation.
    /// </summary>
    public class DeleteMultipleResult
    {
        #region Public-Members

        /// <summary>
        /// Operation status.
        /// </summary>
        public DeleteMultipleStatusEnum Status { get; set; } = DeleteMultipleStatusEnum.Deleted;

        /// <summary>
        /// Number of entities successfully deleted.
        /// </summary>
        public int Deleted { get; set; }

        /// <summary>
        /// Entities that were skipped (not found, actively in use, etc.). At most one per requested id, so bounded by
        /// the request limit.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One entry per requested id at most; request ids are limited by CollectionLimits.RequireRequestItems")]
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

        #region Public-Methods

        /// <summary>
        /// Resolve the Status enum based on Deleted and Skipped counts.
        /// Call this after the delete loop completes.
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
