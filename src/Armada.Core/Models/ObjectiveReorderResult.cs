namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a backlog reorder: how many objectives were updated and the rank each now has. It carries one item per
    /// item of the request, so it is bounded by <see cref="CollectionLimits.MaxRequestItems"/>. Read an objective for
    /// its full record.
    /// </summary>
    public class ObjectiveReorderResult
    {
        /// <summary>
        /// Number of objectives updated.
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// The applied ranks, ordered by rank then objective id.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One item per item of the reorder request, which is limited to MaxRequestItems.")]
        public List<ObjectiveReorderItem> Items { get; set; } = new List<ObjectiveReorderItem>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ObjectiveReorderResult()
        {
        }
    }
}
