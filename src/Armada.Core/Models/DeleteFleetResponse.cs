namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/fleets/{id} or armada_delete_fleet.
    /// </summary>
    public class DeleteFleetResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted fleet.
        /// </summary>
        public string? FleetId { get; set; }
    }
}
