namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What the fleet page loads in one round: the fleet, its vessels, and the pipelines.
    /// </summary>
    public class FleetLoad
    {
        #region Public-Members

        /// <summary>
        /// The fleet, or null when not found.
        /// </summary>
        public Fleet? Fleet { get; set; } = null;

        /// <summary>
        /// Vessels in the fleet.
        /// </summary>
        public List<Vessel> Vessels { get; set; } = new List<Vessel>();

        /// <summary>
        /// Pipelines.
        /// </summary>
        public List<Pipeline> Pipelines { get; set; } = new List<Pipeline>();

        #endregion
    }
}
