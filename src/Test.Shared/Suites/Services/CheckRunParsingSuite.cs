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

            cases.Add(Case("console_text_with_windows_line_endings", "Console summaries with CRLF line endings (output from a Windows host) parse like LF output", TestTags.Positive, () =>
            {
                CheckRunTestSummary? dotnet = CheckRunParsingService.ParseTestSummary("Build succeeded.\r\nPassed!  - Failed:     0, Passed:     3, Skipped:     1, Total:     4, Duration: 1.5 s\r\nDone.\r\n");
                AssertNotNull(dotnet, "dotnet summary with CRLF");
                AssertEqual("dotnet", dotnet!.Format);
                AssertEqual(3, dotnet.Passed);
                AssertEqual(4, dotnet.Total);

                CheckRunTestSummary? pytest = CheckRunParsingService.ParseTestSummary("collected 5 items\r\n===== 4 passed, 1 failed in 0.52s =====\r\n");
                AssertNotNull(pytest, "pytest summary with CRLF");
                AssertEqual(4, pytest!.Passed);
                AssertEqual(1, pytest.Failed);

                CheckRunTestSummary? jest = CheckRunParsingService.ParseTestSummary("RUN  v2\r\nTests  7 passed (7)\r\n");
                AssertNotNull(jest, "jest summary with CRLF");
                AssertEqual(7, jest!.Passed);

                // Each CRLF result matches the same text with LF endings.
                foreach (string text in new[] { "Passed!  - Failed:     0, Passed:     3, Skipped:     1, Total:     4, Duration: 1.5 s\r\n", "===== 4 passed, 1 failed in 0.52s =====\r\n", "Tests  7 passed (7)\r\n" })
                {
                    CheckRunTestSummary? crlf = CheckRunParsingService.ParseTestSummary(text);
                    CheckRunTestSummary? lf = CheckRunParsingService.ParseTestSummary(text.Replace("\r\n", "\n"));
                    AssertEqual(lf?.Total, crlf?.Total, "total for " + text.Trim());
                    AssertEqual(lf?.DurationMs, crlf?.DurationMs, "duration for " + text.Trim());
                }
            }));

            cases.Add(Case("jest_console_summary_parsed", "Jest's console summary (Tests: with a colon) parses and wins over the Test Suites line", TestTags.Positive, () =>
            {
                string output =
                    " PASS  src/math.test.js\n" +
                    " PASS  src/strings.test.js\n" +
                    "\n" +
                    "Test Suites: 2 passed, 2 total\n" +
                    "Tests:       7 passed, 7 total\n" +
                    "Snapshots:   0 total\n" +
                    "Time:        1.234 s\n" +
                    "Ran all test suites.\n";
                CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output);
                AssertNotNull(summary, "jest summary expected");
                AssertEqual("javascript", summary!.Format);
                AssertEqual(7, summary.Passed);
                AssertEqual(0, summary.Failed);
                AssertEqual(0, summary.Skipped);
                AssertEqual(7, summary.Total);
                AssertEqual(1234L, summary.DurationMs);

                CheckRunTestSummary? crlf = CheckRunParsingService.ParseTestSummary(output.Replace("\n", "\r\n"));
                AssertNotNull(crlf, "jest summary with CRLF");
                AssertEqual(7, crlf!.Total);
                AssertEqual(1234L, crlf.DurationMs);
            }));

            cases.Add(Case("jest_console_failing_run_parsed", "A failing Jest run reports failed, skipped, and todo counts from the Tests: line", TestTags.Negative, () =>
            {
                string output =
                    " FAIL  src/math.test.js\r\n" +
                    "  \u25cf math > adds\r\n" +
                    "\r\n" +
                    "    expect(received).toBe(expected) // Object.is equality\r\n" +
                    "\r\n" +
                    " PASS  src/strings.test.js\r\n" +
                    "\r\n" +
                    "Test Suites: 1 failed, 1 passed, 2 total\r\n" +
                    "Tests:       1 failed, 2 skipped, 1 todo, 5 passed, 9 total\r\n" +
                    "Snapshots:   0 total\r\n" +
                    "Time:        2.5 s, estimated 3 s\r\n" +
                    "Ran all test suites.\r\n";
                CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output);
                AssertNotNull(summary, "jest summary expected");
                AssertEqual(5, summary!.Passed);
                AssertEqual(1, summary.Failed);
                AssertEqual(3, summary.Skipped);
                AssertEqual(9, summary.Total);
                AssertEqual(2500L, summary.DurationMs);
            }));

            cases.Add(Case("vitest_console_summary_parsed", "Vitest's indented Tests line parses, with the total in parentheses, over the Test Files line", TestTags.Positive, () =>
            {
                string output =
                    "\n" +
                    " RUN  v1.6.0 /work/app\n" +
                    "\n" +
                    " \u2713 src/math.test.ts  (4 tests) 3ms\n" +
                    " \u2713 src/strings.test.ts  (3 tests) 2ms\n" +
                    "\n" +
                    " Test Files  2 passed (2)\n" +
                    "      Tests  7 passed (7)\n" +
                    "   Start at  10:15:02\n" +
                    "   Duration  412ms (transform 55ms, setup 0ms, collect 80ms, tests 5ms, environment 0ms, prepare 120ms)\n";
                CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output);
                AssertNotNull(summary, "vitest summary expected");
                AssertEqual(7, summary!.Passed);
                AssertEqual(0, summary.Failed);
                AssertEqual(7, summary.Total);
                AssertEqual(412L, summary.DurationMs);

                CheckRunTestSummary? singleSpace = CheckRunParsingService.ParseTestSummary(" Tests  7 passed (7)\r\n");
                AssertNotNull(singleSpace, "vitest line with one leading space");
                AssertEqual(7, singleSpace!.Total);
            }));

            cases.Add(Case("vitest_console_failing_run_parsed", "A failing Vitest run reports failed, skipped, and todo counts and the parenthesized total", TestTags.Negative, () =>
            {
                string output =
                    " FAIL  src/math.test.ts > math > adds\r\n" +
                    "AssertionError: expected 3 to be 4 // Object.is equality\r\n" +
                    "\r\n" +
                    " Test Files  1 failed | 1 passed (2)\r\n" +
                    "      Tests  1 failed | 5 passed | 2 skipped | 1 todo (9)\r\n" +
                    "   Start at  10:15:02\r\n" +
                    "   Duration  1.52s (transform 55ms, setup 0ms, collect 80ms, tests 5ms)\r\n";
                CheckRunTestSummary? summary = CheckRunParsingService.ParseTestSummary(output);
                AssertNotNull(summary, "vitest summary expected");
                AssertEqual(5, summary!.Passed);
                AssertEqual(1, summary.Failed);
                AssertEqual(3, summary.Skipped);
                AssertEqual(9, summary.Total);
                AssertEqual(1520L, summary.DurationMs);
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
