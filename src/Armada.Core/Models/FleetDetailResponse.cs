namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/fleets/{id}.
    /// </summary>
    public class FleetDetailResponse
    {
        /// <summary>
        /// The fleet.
        /// </summary>
        public Fleet? Fleet { get; set; }

        /// <summary>
        /// Vessels belonging to this fleet.
        /// </summary>
        public List<Vessel>? Vessels { get; set; }
    }
}
