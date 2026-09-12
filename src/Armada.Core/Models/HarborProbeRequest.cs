namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request to run a one-off host command on a connected Harbor over its link, for verifying end-to-end
    /// connectivity. Defaults to "git --version". The command is executed on the Harbor host and its result
    /// returned, exercising the full server-issues-work / Harbor-replies loop.
    /// </summary>
    public class HarborProbeRequest
    {
        #region Public-Members

        /// <summary>
        /// Executable to run on the Harbor host. Defaults to "git".
        /// </summary>
        public string Executable { get; set; } = "git";

        /// <summary>
        /// Command-line arguments. Defaults to a single "--version" argument.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string> { "--version" };

        /// <summary>
        /// Working directory on the Harbor host, or empty for the Harbor's default.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Timeout in milliseconds. Defaults to 15000.
        /// </summary>
        public int TimeoutMs { get; set; } = 15000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborProbeRequest()
        {
        }

        #endregion
    }
}
