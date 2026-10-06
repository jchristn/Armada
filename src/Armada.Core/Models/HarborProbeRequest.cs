namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

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

        #region Public-Methods

        /// <summary>
        /// The host command this probe runs. A request body can set any field to JSON null, which deserializes to null
        /// despite the non-nullable declarations; a null field means the same as an omitted one, its default: a blank
        /// executable runs "git", null arguments are "--version", and a null working directory is the Harbor's
        /// default.
        /// </summary>
        /// <returns>The command request; never null, with non-null executable, arguments, and working directory.</returns>
        public HostCommandRequest ToHostCommandRequest()
        {
            List<string>? arguments = Arguments;
            string? workingDirectory = WorkingDirectory;
            return new HostCommandRequest
            {
                Executable = String.IsNullOrWhiteSpace(Executable) ? "git" : Executable.Trim(),
                Arguments = arguments != null ? new List<string>(arguments) : new List<string> { "--version" },
                WorkingDirectory = workingDirectory ?? String.Empty,
                TimeoutMs = TimeoutMs
            };
        }

        #endregion
    }
}
