namespace Armada.Tui.Screens.Entities
{
    /// <summary>
    /// Slot names for <see cref="NavigationPrefill"/> hand-offs between delivery screens (the dashboard's router
    /// state). Each slot documents its payload type.
    /// </summary>
    public static class PrefillSlots
    {
        #region Public-Members

        /// <summary>
        /// Open the Run Check modal on the Checks tab. Payload: <c>Armada.Core.Models.CheckRunRequest</c>. Path:
        /// <c>/delivery?tab=checks</c>.
        /// </summary>
        public const string RunCheck = "checks.run";

        /// <summary>
        /// Create an incident prefilled. Payload: <c>Armada.Core.Models.IncidentUpsertRequest</c>. Path:
        /// <c>/incidents/new</c>.
        /// </summary>
        public const string CreateIncident = "incidents.create";

        /// <summary>
        /// Start a runbook execution prefilled (carried from the Runbooks tab to the runbook the user opens). Payload:
        /// <c>Armada.Core.Models.RunbookExecutionStartRequest</c>. Path: <c>/delivery?tab=runbooks</c>.
        /// </summary>
        public const string RunbookExecution = "runbooks.execution";

        /// <summary>
        /// Create a deployment prefilled. Payload: <c>Armada.Core.Models.DeploymentUpsertRequest</c>. Path:
        /// <c>/deployments/new</c>.
        /// </summary>
        public const string CreateDeployment = "deployments.create";

        /// <summary>
        /// Create a release prefilled. Payload: <c>Armada.Core.Models.ReleaseUpsertRequest</c>. Path:
        /// <c>/releases/new</c>.
        /// </summary>
        public const string CreateRelease = "releases.create";

        #endregion
    }
}
