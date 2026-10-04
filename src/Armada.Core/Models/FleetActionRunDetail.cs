namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A fleet action run with summaries of all of its targets (no output text).
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

        /// <summary>
        /// Target summaries ordered by creation.
        /// </summary>
        public List<FleetActionRunTargetSummary> Targets
        {
            get => _Targets;
            set => _Targets = value ?? new List<FleetActionRunTargetSummary>();
        }

        #endregion

        #region Private-Members

        private FleetActionRun _Run = new FleetActionRun();
        private List<FleetActionRunTargetSummary> _Targets = new List<FleetActionRunTargetSummary>();

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
