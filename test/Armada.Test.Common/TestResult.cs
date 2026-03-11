namespace Armada.Test.Common
{
    /// <summary>
    /// Data class holding the result of a single test execution.
    /// </summary>
    public class TestResult
    {
        #region Public-Members

        /// <summary>
        /// Name of the test.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Whether the test passed.
        /// </summary>
        public bool Passed { get; set; } = false;

        /// <summary>
        /// Optional message describing the result.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Exception that caused the failure, if any.
        /// </summary>
        public Exception? Exception { get; set; } = null;

        /// <summary>
        /// Elapsed time in milliseconds.
        /// </summary>
        public long ElapsedMs { get; set; } = 0;

        #endregion
    }
}
