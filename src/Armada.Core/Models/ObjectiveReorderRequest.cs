namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Batch rank updates for backlog/objective reordering.
    /// </summary>
    public class ObjectiveReorderRequest
    {
        /// <summary>
        /// Rank updates to apply.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<ObjectiveReorderItem> Items { get; set; } = new List<ObjectiveReorderItem>();
    }
}
