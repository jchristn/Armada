namespace Armada.Core.Models
{
    /// <summary>
    /// Result of running a test command, containing exit code and output.
    /// </summary>
    public class TestResult
    {
        #region Public-Members

        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Combined stdout and stderr output.
        /// </summary>
        public string Output { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TestResult()
        {
        }

        /// <summary>
        /// Instantiate with exit code and output.
        /// </summary>
        /// <param name="exitCode">Process exit code.</param>
        /// <param name="output">Combined output.</param>
        public TestResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? "";
        }

        #endregion
    }
}
