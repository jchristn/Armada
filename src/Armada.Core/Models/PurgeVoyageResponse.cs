namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/voyages/{id}/purge or armada_purge_voyage.
    /// </summary>
    public class PurgeVoyageResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the purged voyage.
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Number of missions deleted.
        /// </summary>
        public int MissionsDeleted { get; set; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? Error { get; set; }
    }
}
