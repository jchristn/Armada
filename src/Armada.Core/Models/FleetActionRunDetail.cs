namespace Armada.Core.Models
{
    /// <summary>
    /// A fleet action run as returned by the run read. The run's targets are not embedded (docs/ENUMERATION.md): the
    /// run carries the target counts, and the targets are read one page at a time from
    /// <c>POST /api/v1/fleet-action-runs/{id}/targets/enumerate</c> (MCP <c>enumerate</c> with entityType
    /// <c>fleet_action_run_target</c>).
    /// </summary>
    public class FleetActionRunDetail
    {
        #region Public-Members

        /// <summary>
        /// The run.
        /// </summary>
        public FleetActionRun Run
        {
            get => _Run;
            set => _Run = value ?? new FleetActionRun();
        }

        #endregion

        #region Private-Members

        private FleetActionRun _Run = new FleetActionRun();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunDetail()
        {
        }

        #endregion
    }
}
