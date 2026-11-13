namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// GET /api/v1/fleets/{id} response: the fleet and its vessel count. The vessels are not embedded; read them one
    /// page at a time with <c>EnumerateVesselsAsync</c> and a fleetId filter.
    /// </summary>
    public class FleetDetail
    {
        #region Public-Members

        /// <summary>
        /// The fleet, or null.
        /// </summary>
        public Armada.Core.Models.Fleet? Fleet { get; set; } = null;

        /// <summary>
        /// Number of vessels in the fleet visible to the caller.
        /// </summary>
        public long VesselCount { get; set; } = 0;

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
