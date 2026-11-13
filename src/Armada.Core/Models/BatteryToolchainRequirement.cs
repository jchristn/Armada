namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A tool the Harbor needs, with the command that proves it is installed.
    /// </summary>
    public class BatteryToolchainRequirement
    {
        #region Public-Members

        /// <summary>
        /// Tool name, referenced by tasks.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Executable of the probe command.
        /// </summary>
        public string? Executable { get; set; } = null;

        /// <summary>
        /// Arguments of the probe command; exit code 0 means the tool is installed.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new List<string>();
        }

        /// <summary>
        /// What to install when it is missing.
        /// </summary>
        public string? Hint { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Arguments = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryToolchainRequirement()
        {
        }

        #endregion
    }
}
