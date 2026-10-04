namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Optional filters for a vessel readiness check.
    /// </summary>
    public class VesselReadinessQuery
    {
        #region Public-Members

        /// <summary>
        /// Workflow profile id, or null.
        /// </summary>
        public string? WorkflowProfileId { get; set; } = null;

        /// <summary>
        /// Check type, or null.
        /// </summary>
        public string? CheckType { get; set; } = null;

        /// <summary>
        /// Environment name, or null.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// Include workflow requirements, or null.
        /// </summary>
        public bool? IncludeWorkflowRequirements { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselReadinessQuery()
        {
        }

        #endregion
    }
}
