namespace Test.Shared.Suites.Tui.Bodies
{
    using System.Collections.Generic;

    /// <summary>
    /// Arguments of the dispatch quick action.
    /// </summary>
    public class AskDispatchArgumentsBody
    {
        #region Public-Members

        /// <summary>
        /// Voyage title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Missions.
        /// </summary>
        public List<AskDispatchMissionArgumentsBody>? Missions { get; set; } = null;

        /// <summary>
        /// Pipeline id.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        #endregion
    }
}
