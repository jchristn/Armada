namespace Armada.Tui.Screens.Delivery
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Reference lists behind the runbook forms and the Runbooks grid's binding column.
    /// </summary>
    public class RunbookReferenceData
    {
        #region Public-Members

        /// <summary>
        /// Workflow profiles.
        /// </summary>
        public List<WorkflowProfile> Profiles { get; set; } = new List<WorkflowProfile>();

        /// <summary>
        /// Environments.
        /// </summary>
        public List<DeploymentEnvironment> Environments { get; set; } = new List<DeploymentEnvironment>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RunbookReferenceData()
        {
        }

        #endregion
    }
}
