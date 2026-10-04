namespace Armada.Tui.Services.Credentials
{
    /// <summary>
    /// Outcome of a helper process run by <see cref="ProcessRunner"/>.
    /// </summary>
    public class ProcessResult
    {
        #region Public-Members

        /// <summary>
        /// Exit code, or -1 when the process could not start or timed out.
        /// </summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>
        /// Standard output.
        /// </summary>
        public string StdOut { get; set; } = "";

        /// <summary>
        /// Standard error.
        /// </summary>
        public string StdErr { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ProcessResult()
        {
        }

        #endregion
    }
}
