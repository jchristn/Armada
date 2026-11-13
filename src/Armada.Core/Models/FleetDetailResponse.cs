namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Response from GET /api/v1/fleets/{id} (and MCP get_fleet, WebSocket get_fleet): the fleet and how many vessels
    /// it holds in the caller's scope. The vessels themselves are not embedded; read them one page at a time with
    /// POST /api/v1/vessels/enumerate (or GET /api/v1/vessels) and fleetId (docs/ENUMERATION.md).
    /// </summary>
    public class FleetDetailResponse
    {
        #region Public-Members

        /// <summary>
        /// The fleet.
        /// </summary>
        public Fleet? Fleet { get; set; }

        /// <summary>
        /// Number of vessels in this fleet visible to the caller (the TotalRecords of the vessels enumeration filtered
        /// by fleetId). Minimum 0.
        /// </summary>
        public long VesselCount
        {
            get => _VesselCount;
            set => _VesselCount = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private long _VesselCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetDetailResponse()
        {
        }

        #endregion
    }
}
