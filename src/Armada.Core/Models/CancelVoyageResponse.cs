namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/voyages/{id} or armada_cancel_voyage.
    /// </summary>
    public class CancelVoyageResponse
    {
        /// <summary>
        /// The cancelled voyage.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Number of missions that were cancelled.
        /// </summary>
        public int CancelledMissions { get; set; }
    }
}
