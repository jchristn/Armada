namespace Armada.Core.Models
{
    /// <summary>
    /// Per-run overrides applied on top of the saved action definition. They change only the run snapshot, never
    /// the saved action.
    /// </summary>
    public class FleetActionRunOverrides
    {
        #region Public-Members

        /// <summary>
        /// Per-target timeout in seconds; clamped 5 to 7200.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Whether targets with a dirty working tree are skipped.
        /// </summary>
        public bool? RequiresCleanWorkingTree { get; set; } = null;

        /// <summary>
        /// Pipeline identifier for Mission runs. An empty string clears the action's pipeline for this run.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunOverrides()
        {
        }

        #endregion
    }
}
