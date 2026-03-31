namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/vessels/{id} or armada_delete_vessel.
    /// </summary>
    public class DeleteVesselResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted vessel.
        /// </summary>
        public string? VesselId { get; set; }
    }
}
