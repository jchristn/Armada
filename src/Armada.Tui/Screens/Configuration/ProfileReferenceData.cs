namespace Armada.Tui.Screens.Configuration
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Reference lists behind the workflow and project profile forms.
    /// </summary>
    public class ProfileReferenceData
    {
        #region Public-Members

        /// <summary>
        /// Active fleets.
        /// </summary>
        public List<Fleet> Fleets { get; set; } = new List<Fleet>();

        /// <summary>
        /// Active vessels.
        /// </summary>
        public List<Vessel> Vessels { get; set; } = new List<Vessel>();

        /// <summary>
        /// Pipelines (project profiles).
        /// </summary>
        public List<Pipeline> Pipelines { get; set; } = new List<Pipeline>();

        /// <summary>
        /// Workflow profiles (project profiles).
        /// </summary>
        public List<WorkflowProfile> WorkflowProfiles { get; set; } = new List<WorkflowProfile>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProfileReferenceData()
        {
        }

        #endregion
    }
}
