namespace Armada.Tui.Screens.Delivery
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Reference lists behind the incident forms and the Incidents grid's name columns.
    /// </summary>
    public class IncidentReferenceData
    {
        #region Public-Members

        /// <summary>
        /// Vessels.
        /// </summary>
        public List<Vessel> Vessels { get; set; } = new List<Vessel>();

        /// <summary>
        /// Environments.
        /// </summary>
        public List<DeploymentEnvironment> Environments { get; set; } = new List<DeploymentEnvironment>();

        /// <summary>
        /// Deployments.
        /// </summary>
        public List<Deployment> Deployments { get; set; } = new List<Deployment>();

        /// <summary>
        /// Releases.
        /// </summary>
        public List<Release> Releases { get; set; } = new List<Release>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public IncidentReferenceData()
        {
        }

        #endregion
    }
}
