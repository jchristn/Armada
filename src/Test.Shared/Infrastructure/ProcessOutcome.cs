namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Exit code and combined output of a child process run by a test.
    /// </summary>
    public sealed class ProcessOutcome
    {
        /// <summary>
        /// Exit code, or -1 when the process did not exit in time.
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// Standard output followed by standard error.
        /// </summary>
        public string Output { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="exitCode">Exit code.</param>
        /// <param name="output">Combined output.</param>
        public ProcessOutcome(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? "";
        }
    }
}
