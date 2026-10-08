namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor-to-server reply to a git/gh request: the exit code and captured output.
    /// </summary>
    public class HarborGitResult : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier this result answers.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Process exit code (0 on success).
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
        /// Whether the Harbor stopped the command because it ran past the request's timeout.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        #endregion
    }
}
