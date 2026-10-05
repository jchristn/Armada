namespace Armada.Server.RuntimeTools
{
    using System;

    /// <summary>
    /// Exit code and captured output of a runtime CLI invocation.
    /// </summary>
    public class RuntimeCommandResult
    {
        #region Public-Members

        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Captured standard output, trimmed.
        /// </summary>
        public string Stdout { get; set; } = String.Empty;

        /// <summary>
        /// Captured standard error, trimmed.
        /// </summary>
        public string Stderr { get; set; } = String.Empty;

        #endregion
    }
}
