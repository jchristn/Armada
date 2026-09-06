namespace Armada.Runtimes.Tools.Tasks
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of validating a proposed task plan: whether it is usable and, when not, the ordered
    /// list of human-readable problems that made it invalid. The problems are returned to the model so it
    /// can correct and re-send the plan.
    /// </summary>
    public class TaskPlanValidationResult
    {
        #region Private-Members

        private List<string> _Problems = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the plan is valid — true when there are no recorded problems.
        /// </summary>
        public bool IsValid => _Problems.Count == 0;

        /// <summary>
        /// The validation problems, in the order they were discovered. Never null; empty when valid.
        /// </summary>
        public List<string> Problems
        {
            get => _Problems;
            set => _Problems = value ?? new List<string>();
        }

        #endregion
    }
}
