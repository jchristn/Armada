namespace Armada.Tui.Ask
{
    using System.Collections.Generic;

    /// <summary>
    /// The Dispatch quick-action form's values (the dashboard's <c>DispatchDraft</c>).
    /// </summary>
    public class AskDispatchDraft
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Voyage title (optional; defaults to the first mission title).
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Pipeline id (optional; empty uses the vessel default).
        /// </summary>
        public string PipelineId { get; set; } = "";

        /// <summary>
        /// Missions. Never null.
        /// </summary>
        public List<AskDispatchMissionDraft> Missions { get; set; } = new List<AskDispatchMissionDraft> { new AskDispatchMissionDraft() };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskDispatchDraft()
        {
        }

        #endregion
    }
}
