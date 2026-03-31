namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/voyages/{id} or armada_voyage_status.
    /// </summary>
    public class VoyageDetailResponse
    {
        /// <summary>
        /// The voyage.
        /// </summary>
        public Voyage? Voyage { get; set; }

        /// <summary>
        /// Missions in this voyage.
        /// </summary>
        public List<Mission>? Missions { get; set; }
    }
}
