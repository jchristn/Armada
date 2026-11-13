namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// What one vessel cleanup scan or run read: the inventory counts, the run's per-item outcomes, and the first page
    /// of each disposition group. Built off the UI loop and applied on it.
    /// </summary>
    public class VesselCleanupSnapshot
    {
        /// <summary>
        /// Inventory counts, or null.
        /// </summary>
        public VesselCleanupInventory? Inventory { get; set; } = null;

        /// <summary>
        /// Run outcomes, or null for a scan.
        /// </summary>
        public VesselCleanupResult? Result { get; set; } = null;

        /// <summary>
        /// First page of each group that was read.
        /// </summary>
        public Dictionary<VesselCleanupDispositionEnum, EnumerationResult<VesselCleanupResource>> Pages { get; } = new Dictionary<VesselCleanupDispositionEnum, EnumerationResult<VesselCleanupResource>>();
    }
}
