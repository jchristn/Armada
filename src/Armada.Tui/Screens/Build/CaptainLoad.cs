namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What the captain page loads in one round: the captain, its current mission, and its recent missions.
    /// </summary>
    public class CaptainLoad
    {
        #region Public-Members

        /// <summary>
        /// The captain, or null when not found.
        /// </summary>
        public Captain? Captain { get; set; } = null;

        /// <summary>
        /// The current mission, or null.
        /// </summary>
        public Mission? CurrentMission { get; set; } = null;

        /// <summary>
        /// Recent missions (up to 100).
        /// </summary>
        public List<MissionSummary> Missions { get; set; } = new List<MissionSummary>();

        #endregion
    }
}
