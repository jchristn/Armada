namespace Armada.Tui.Screens.Build
{
    using System.Collections.Generic;

    /// <summary>
    /// One editable fleet recommendation (the dashboard's <c>FleetDraft</c>): name, description, the captain's
    /// rationale, the vessels in it, and the fleet it was applied to.
    /// </summary>
    public class ImportFleetDraft
    {
        #region Public-Members

        /// <summary>
        /// Stable key.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Fleet name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Why the captain grouped these.
        /// </summary>
        public string Rationale { get; set; } = "";

        /// <summary>
        /// Vessel ids.
        /// </summary>
        public List<string> VesselIds { get; set; } = new List<string>();

        /// <summary>
        /// Fleet the recommendation was applied to, or null.
        /// </summary>
        public string? AppliedFleetId { get; set; } = null;

        #endregion
    }
}
