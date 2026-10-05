namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What the vessel page loads in one round: the vessel, fleets, its missions, and pipelines.
    /// </summary>
    public class VesselLoad
    {
        #region Public-Members

        /// <summary>
        /// The vessel, or null when not found.
        /// </summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>
        /// Fleets.
        /// </summary>
        public List<Fleet> Fleets { get; set; } = new List<Fleet>();

        /// <summary>
        /// The vessel's missions.
        /// </summary>
        public List<MissionSummary> Missions { get; set; } = new List<MissionSummary>();

        /// <summary>
        /// Pipelines.
        /// </summary>
        public List<Pipeline> Pipelines { get; set; } = new List<Pipeline>();

        #endregion
    }
}
