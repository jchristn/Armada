namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/fleets/{id} response: the fleet and its vessels (the server returns this shape although the
    /// dashboard types the call as a bare Fleet).
    /// </summary>
    public class FleetDetail
    {
        #region Public-Members

        /// <summary>
        /// The fleet, or null.
        /// </summary>
        public Armada.Core.Models.Fleet? Fleet { get; set; } = null;

        /// <summary>
        /// Vessels in the fleet. Never null.
        /// </summary>
        public List<Armada.Core.Models.Vessel> Vessels { get; set; } = new List<Armada.Core.Models.Vessel>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetDetail()
        {
        }

        #endregion
    }
}
