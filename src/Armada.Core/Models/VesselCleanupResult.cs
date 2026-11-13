namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a vessel cleanup: per-item outcomes and the refreshed inventory.
    /// </summary>
    public class VesselCleanupResult
    {
        /// <summary>
        /// Per-item outcomes in the order they ran.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "One per distinct requested key; Keys is limited to MaxRequestItems")]
        public List<VesselCleanupItemResult> Results { get; set; } = new List<VesselCleanupItemResult>();

        /// <summary>
        /// Number of resources removed.
        /// </summary>
        public int Deleted { get; set; } = 0;

        /// <summary>
        /// Number of resources skipped by revalidation.
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Number of resources whose removal failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Inventory rescanned after cleanup.
        /// </summary>
        public VesselCleanupInventory Inventory { get; set; } = new VesselCleanupInventory();
    }
}
