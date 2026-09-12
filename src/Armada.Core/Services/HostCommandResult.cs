namespace Armada.Core.Services
{
    /// <summary>
    /// The result of running a host command: exit code and captured output, plus whether it was killed for
    /// exceeding its timeout.
    /// </summary>
    public class HostCommandResult
    {
        #region Public-Members

        /// <summary>
        /// Process exit code (0 on success). -1 when the command timed out or could not be observed.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Captured standard output.
        /// </summary>
        public string StandardOutput { get; set; } = string.Empty;

        /// <summary>
        /// Captured standard error.
        /// </summary>
        public string StandardError { get; set; } = string.Empty;

        /// <summary>
        /// Whether the command was killed for exceeding its timeout.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Whether the command completed successfully (exit code 0 and not timed out).
        /// </summary>
        public bool Success => ExitCode == 0 && !TimedOut;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HostCommandResult()
        {
        }

        #endregion
    }
}
