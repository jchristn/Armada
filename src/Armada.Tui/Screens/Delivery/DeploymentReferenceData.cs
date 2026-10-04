namespace Armada.Tui.Screens.Delivery
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Reference lists behind the deployment form and the Deployments grid's name columns.
    /// </summary>
    public class DeploymentReferenceData
    {
        #region Public-Members

        /// <summary>
        /// Vessels.
        /// </summary>
        public List<Vessel> Vessels { get; set; } = new List<Vessel>();

        /// <summary>
        /// Workflow profiles.
        /// </summary>
        public List<WorkflowProfile> Profiles { get; set; } = new List<WorkflowProfile>();

        /// <summary>
        /// Environments.
        /// </summary>
        public List<DeploymentEnvironment> Environments { get; set; } = new List<DeploymentEnvironment>();

        /// <summary>
        /// Releases.
        /// </summary>
        public List<Release> Releases { get; set; } = new List<Release>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentReferenceData()
        {
        }

        #endregion
    }
}
