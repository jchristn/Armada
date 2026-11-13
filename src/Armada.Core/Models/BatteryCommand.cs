namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A command a grader runs in the task's worktree.
    /// </summary>
    public class BatteryCommand
    {
        #region Public-Members

        /// <summary>
        /// Executable.
        /// </summary>
        public string? Executable { get; set; } = null;

        /// <summary>
        /// Arguments.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new List<string>();
        }

        /// <summary>
        /// Exit code that counts as success.
        /// </summary>
        public int ExpectedExitCode { get; set; } = 0;

        /// <summary>
        /// Timeout in seconds, or null for 600.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Arguments = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryCommand()
        {
        }

        #endregion
    }
}
