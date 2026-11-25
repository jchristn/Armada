namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Touchstone 0.2.1 skips a case only when its <c>skip</c> flag is true; a <c>skipReason</c> alone is recorded but
    /// the case still runs. This suite scans the shared test sources so a case that sets a skip reason always sets the
    /// flag too (the real pi smoke cases ran on hosts without pi because of this).
    /// </summary>
    public sealed class TestSkipFlagHygieneSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Models.TestSkipFlagHygiene";

        /// <summary>
        /// Build the descriptor for the skip flag hygiene suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(
                    suiteId: SuiteId,
                    caseId: "skip_reason_sets_skip_flag",
                    displayName: "Every test source that passes skipReason also passes the skip flag",
                    executeAsync: (System.Threading.CancellationToken token) =>
                    {
                        string root = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Test.Shared");
                        List<string> offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                            .Where(path =>
                            {
                                string text = File.ReadAllText(path);
                                return text.Contains("skipReason:", StringComparison.Ordinal) && !text.Contains("skip:", StringComparison.Ordinal);
                            })
                            .Select(path => Path.GetRelativePath(root, path))
                            .OrderBy(path => path, StringComparer.Ordinal)
                            .ToList();
                        AssertTrue(offenders.Count == 0, "files that pass skipReason without skip: " + String.Join(", ", offenders));
                        return Task.CompletedTask;
                    })
            };

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Test skip flag hygiene", cases: cases);
        }
    }
}
