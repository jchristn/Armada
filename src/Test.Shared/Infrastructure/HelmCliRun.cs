namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Outcome of an in-process <c>armada</c> CLI run (<see cref="HelmCliHarness"/>).
    /// </summary>
    public sealed class HelmCliRun
    {
        #region Public-Members

        /// <summary>
        /// Exit code.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Text written through the Spectre console (markup rendered without colors).
        /// </summary>
        public string Output { get; set; } = String.Empty;

        /// <summary>
        /// Text written to standard output directly (for example --json documents).
        /// </summary>
        public string StandardOutput { get; set; } = String.Empty;

        #endregion
    }
}
