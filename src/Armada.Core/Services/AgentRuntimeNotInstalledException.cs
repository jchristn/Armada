namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Raised when an agent runtime's CLI cannot be started because its executable is not installed on this host or is
    /// not on its PATH (the operating system reported "file not found" for the executable, not for the working
    /// directory). Callers turn it into a user-facing message that names the host it ran on.
    /// </summary>
    public class AgentRuntimeNotInstalledException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The runtime whose CLI is missing.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; }

        /// <summary>
        /// The executable that could not be started (as configured, for example "claude").
        /// </summary>
        public string Executable { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runtime">The runtime whose CLI is missing.</param>
        /// <param name="executable">The executable that could not be started.</param>
        /// <param name="inner">The process-start failure.</param>
        public AgentRuntimeNotInstalledException(AgentRuntimeEnum runtime, string executable, Exception? inner)
            : base("The " + runtime + " CLI ('" + executable + "') is not installed on this host or is not on its PATH.", inner)
        {
            Runtime = runtime;
            Executable = executable ?? String.Empty;
        }

        #endregion
    }
}
