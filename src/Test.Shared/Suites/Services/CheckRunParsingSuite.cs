namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="CheckRunParsingService"/>: structured result artifacts (TRX, Jest JSON) win over
    /// console summary text, and Istanbul coverage JSON is read through typed classes (including the "Unknown"
    /// percentage Istanbul writes for an empty total).
    /// </summary>
    public sealed class CheckRunParsingSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the check-run parsing suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("trx_artifact_preferred_over_console_text", "A TRX artifact wins over a console summary line", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("checkrun-parse");
                try
                {
                    File.WriteAllText(Path.Combine(root, "results.trx"),
                        "<?xml version=\"1.0\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" +
                        "<ResultSummary outcome=\"Failed\"><Counters total=\"12\" passed=\"10\" failed=\"2\" notExecuted=\"0\" /></ResultSummary></TestRun>");
                    // Console text from a different (for example earlier or partial) run disagrees with the artifact.
                    string output = "Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s";

                    CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output, root, Artifacts("results.trx"));
                    AssertNotNull(summary, "summary expected");
                    AssertEqual("trx", summary!.Format);
                    AssertEqual(12, summary.Total);
                    AssertEqual(2, summary.Failed);
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(Case("console_text_used_without_artifacts", "Console summary text is the fallback when no artifact parses", TestTags.Negative, () =>
            {
                string output = "Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 1 s";
                CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output, null, null);
                AssertNotNull(summary, "summary expected");
                AssertEqual("dotnet", summary!.Format);
                AssertEqual(3, summary.Total);
            }));

            cases.Add(Case("jest_json_artifact_parsed", "A Jest JSON report artifact is parsed into typed counts", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("checkrun-parse");
                try
                {
                    File.WriteAllText(Path.Combine(root, "jest-results.json"),
                        "{\"numTotalTests\":9,\"numPassedTests\":6,\"numFailedTests\":1,\"numPendingTests\":1,\"numTodoTests\":1,\"success\":false,\"testResults\":[]}");
                    CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary("Tests: 99 passed, 99 total", root, Artifacts("jest-results.json"));
                    AssertNotNull(summary, "summary expected");
                    AssertEqual("jest-json", summary!.Format);
                    AssertEqual(9, summary.Total);
                    AssertEqual(6, summary.Passed);
                    AssertEqual(1, summary.Failed);
                    AssertEqual(2, summary.Skipped);
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            cases.Add(Case("istanbul_summary_with_unknown_pct_parsed", "Istanbul coverage JSON with an \"Unknown\" percentage still parses", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("checkrun-parse");
                try
                {
                    File.WriteAllText(Path.Combine(root, "coverage-summary.json"),
                        "{\"total\":{\"lines\":{\"total\":10,\"covered\":8,\"skipped\":0,\"pct\":80}," +
                        "\"branches\":{\"total\":0,\"covered\":0,\"skipped\":0,\"pct\":\"Unknown\"}," +
                        "\"functions\":{\"total\":4,\"covered\":4,\"skipped\":0,\"pct\":100}," +
                        "\"statements\":{\"total\":12,\"covered\":9,\"skipped\":0,\"pct\":75}}}");
                    CheckRunCoverageSummary? coverage = CheckRunParsingService.ParseCoverageSummary(root, Artifacts("coverage-summary.json"));
                    AssertNotNull(coverage, "coverage expected even when one metric has an Unknown percentage");
                    AssertEqual("istanbul-summary", coverage!.Format);
                    AssertNotNull(coverage.Lines, "lines metric");
                    AssertEqual(8, coverage.Lines!.Covered);
                    AssertEqual(10, coverage.Lines.Total);
                    AssertNotNull(coverage.Statements, "statements metric");
                    AssertEqual(9, coverage.Statements!.Covered);
                }
                finally
                {
                    TestTemp.TryDelete(root);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.CheckRunParsing",
                displayName: "Check Run Parsing",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<CheckRunArtifact> Artifacts(params string[] paths)
        {
            List<CheckRunArtifact> artifacts = new List<CheckRunArtifact>();
            foreach (string path in paths) artifacts.Add(new CheckRunArtifact { Path = path });
            return artifacts;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.CheckRunParsing",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
