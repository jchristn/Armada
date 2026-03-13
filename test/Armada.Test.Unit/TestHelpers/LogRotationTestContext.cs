namespace Armada.Test.Unit.TestHelpers
{
    using Armada.Core.Services;

    /// <summary>
    /// Holds the test directory and log rotation service for log rotation tests.
    /// </summary>
    public class LogRotationTestContext
    {
        #region Public-Members

        /// <summary>
        /// Temporary test directory path.
        /// </summary>
        public string TestDir { get; set; } = "";

        /// <summary>
        /// Log rotation service instance.
        /// </summary>
        public LogRotationService Service { get; set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public LogRotationTestContext()
        {
        }

        /// <summary>
        /// Instantiate with test directory and service.
        /// </summary>
        /// <param name="testDir">Temporary test directory path.</param>
        /// <param name="service">Log rotation service instance.</param>
        public LogRotationTestContext(string testDir, LogRotationService service)
        {
            TestDir = testDir ?? "";
            Service = service ?? throw new System.ArgumentNullException(nameof(service));
        }

        #endregion
    }
}
