namespace Armada.Tui.Screens.Operations
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// The results of one Home load (each part null when its call failed, as the dashboard tolerates).
    /// </summary>
    public class HomeData
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public ArmadaStatus? Status { get; set; } = null;

        /// <summary>
        /// Recent missions.
        /// </summary>
        public List<MissionSummary>? Missions { get; set; } = null;

        /// <summary>
        /// True when the recent missions call succeeded.
        /// </summary>
        public bool MissionsLoaded { get; set; } = false;

        /// <summary>
        /// Pending fleet action runs (page of one).
        /// </summary>
        public EnumerationResult<FleetActionRun>? Pending { get; set; } = null;

        /// <summary>
        /// Running fleet action runs (page of one).
        /// </summary>
        public EnumerationResult<FleetActionRun>? Running { get; set; } = null;

        /// <summary>
        /// Vessel health summary.
        /// </summary>
        public VesselHealthSummary? Health { get; set; } = null;

        #endregion
    }
}
