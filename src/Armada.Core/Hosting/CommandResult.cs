namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// Result of running one service-manager command (systemctl, launchctl, sc.exe, reg.exe).
    /// </summary>
    public class CommandResult
    {
        #region Public-Members

        /// <summary>
        /// Process exit code. -1 when the process could not be started.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Captured standard output. Never null.
        /// </summary>
        public string StandardOutput
        {
            get { return _StandardOutput; }
            set { _StandardOutput = value ?? String.Empty; }
        }

        /// <summary>
        /// Captured standard error. Never null.
        /// </summary>
        public string StandardError
        {
            get { return _StandardError; }
            set { _StandardError = value ?? String.Empty; }
        }

        /// <summary>
        /// True when the exit code is zero.
        /// </summary>
        public bool Succeeded
        {
            get { return ExitCode == 0; }
        }

        #endregion

        #region Private-Members

        private string _StandardOutput = String.Empty;
        private string _StandardError = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty, successful result.
        /// </summary>
        public CommandResult()
        {
        }

        /// <summary>
        /// Instantiate a result.
        /// </summary>
        /// <param name="exitCode">Exit code.</param>
        /// <param name="standardOutput">Standard output.</param>
        /// <param name="standardError">Standard error.</param>
        public CommandResult(int exitCode, string? standardOutput, string? standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? String.Empty;
            StandardError = standardError ?? String.Empty;
        }

        #endregion
    }
}
