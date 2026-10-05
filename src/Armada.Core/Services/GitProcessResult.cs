namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Outcome of a git or gh process run that does not throw on a non-zero exit code.
    /// </summary>
    public class GitProcessResult
    {
        #region Public-Members

        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Captured standard output.
        /// </summary>
        public string StandardOutput { get; set; } = String.Empty;

        /// <summary>
        /// Captured standard error.
        /// </summary>
        public string StandardError { get; set; } = String.Empty;

        /// <summary>
        /// True when the exit code is zero.
        /// </summary>
        public bool Succeeded => ExitCode == 0;

        #endregion
    }
}
