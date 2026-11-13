namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Read-only cleanup inventory summary of one vessel's docks, worktrees, and Armada-managed branches. The resources
    /// themselves are not embedded; page them with GET /api/v1/vessels/{id}/cleanup/resources (or POST
    /// .../cleanup/resources/enumerate), optionally filtered by disposition.
    /// </summary>
    public class VesselCleanupInventory
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string VesselName { get; set; } = string.Empty;

        /// <summary>
        /// When the scan ran.
        /// </summary>
        public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of inventoried resources (SafeCount + InUseCount + UnknownCount).
        /// </summary>
        public int ResourceCount { get; set; } = 0;

        /// <summary>
        /// Number of resources safe to clean up.
        /// </summary>
        public int SafeCount { get; set; } = 0;

        /// <summary>
        /// Number of resources still in use.
        /// </summary>
        public int InUseCount { get; set; } = 0;

        /// <summary>
        /// Number of resources with unknown disposition.
        /// </summary>
        public int UnknownCount { get; set; } = 0;

        /// <summary>
        /// Total measured bytes of safe resources (resources without a measured size are not counted).
        /// </summary>
        public long SafeMeasuredBytes { get; set; } = 0;

        /// <summary>
        /// Number of safe resources whose size could not be measured.
        /// </summary>
        public int SafeUnmeasuredCount { get; set; } = 0;
    }
}
