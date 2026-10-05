namespace Test.Shared.Suites.Tui.Bodies
{
    using System.Collections.Generic;

    /// <summary>
    /// Arguments of the run_fleet_action quick action.
    /// </summary>
    public class AskFleetActionArgumentsBody
    {
        #region Public-Members

        /// <summary>
        /// Fleet action id.
        /// </summary>
        public string? ActionId { get; set; } = null;

        /// <summary>
        /// Vessel ids.
        /// </summary>
        public List<string>? VesselIds { get; set; } = null;

        #endregion
    }
}
