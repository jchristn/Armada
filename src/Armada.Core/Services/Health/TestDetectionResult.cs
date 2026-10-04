namespace Armada.Core.Services.Health
{
    using System.Collections.Generic;

    /// <summary>
    /// What the test infrastructure detectors found in a repository.
    /// </summary>
    public class TestDetectionResult
    {
        #region Public-Members

        /// <summary>
        /// Number of recognized projects (.NET project files, package.json files, Python projects, go.mod, Cargo.toml).
        /// </summary>
        public int RecognizedProjects { get; set; } = 0;

        /// <summary>
        /// Number of test indicators found (test projects, test configuration, test directories, or test files).
        /// </summary>
        public int TestIndicators { get; set; } = 0;

        /// <summary>
        /// Ecosystems in which tests were found (DotNet, Node, Python, Go, Rust). Never null.
        /// </summary>
        public List<string> EcosystemsWithTests { get; } = new List<string>();

        /// <summary>
        /// Ecosystems recognized in the repository. Never null.
        /// </summary>
        public List<string> RecognizedEcosystems { get; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TestDetectionResult()
        {
        }

        #endregion
    }
}
