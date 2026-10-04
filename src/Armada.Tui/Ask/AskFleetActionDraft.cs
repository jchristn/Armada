namespace Armada.Tui.Ask
{
    using System.Collections.Generic;

    /// <summary>
    /// The Fleet action quick-action form's values (the dashboard's <c>FleetActionDraft</c>).
    /// </summary>
    public class AskFleetActionDraft
    {
        #region Public-Members

        /// <summary>
        /// Fleet action id.
        /// </summary>
        public string ActionId { get; set; } = "";

        /// <summary>
        /// Chosen vessel ids, in selection order. Never null.
        /// </summary>
        public List<string> VesselIds { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskFleetActionDraft()
        {
        }

        #endregion
    }
}
