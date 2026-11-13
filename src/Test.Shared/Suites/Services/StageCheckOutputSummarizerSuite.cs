namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="StageCheckOutputSummarizer"/>: long check output keeps its head and tail, and the
    /// failing test names from common runners survive in a "Failing tests" section even when they sit in the middle.
    /// </summary>
    public sealed class StageCheckOutputSummarizerSuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Services.StageCheckOutputSummarizer";

        /// <summary>
        /// Build the descriptor for the stage check output summarizer suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("short_output_unchanged", "Output within the budget is returned unchanged", TestTags.Positive, () =>
            {
                string output = "Build succeeded.\n  Failed Foo.Bar [3 ms]\nTotal: 1\n";
                AssertEqual(output.Trim(), StageCheckOutputSummarizer.Summarize(output));
            }));

            cases.Add(Case("vstest_failure_in_middle_survives", "A dotnet test failure buried mid-output is listed with its error message", TestTags.Negative, () =>
            {
                string output = Noise("build line", 200) +
                    "  Failed Garrison.Tests.SeedingTests.PromptTemplatesSeedOnce [12 ms]\n" +
                    "  Error Message:\n" +
                    "   Assert.Equal() Failure: Expected 1, Actual 2\n" +
                    Noise("stack frame", 200) +
                    "Failed!  - Failed:     1, Passed:   511, Skipped:     0, Total:   512\n";
                string summary = StageCheckOutputSummarizer.Summarize(output);
                AssertTrue(summary.StartsWith(StageCheckOutputSummarizer.FailingTestsHeading, StringComparison.Ordinal), "section leads the summary");
                AssertContains("Failed Garrison.Tests.SeedingTests.PromptTemplatesSeedOnce", summary);
                AssertContains("Assert.Equal() Failure: Expected 1, Actual 2", summary, "the line after Error Message: is kept");
                AssertContains("Failed!  - Failed:     1", summary, "tail kept");
                AssertContains("build line 0", summary, "head kept");
                AssertContains("characters omitted", summary);
                AssertTrue(summary.Length < output.Length, "output was shortened");
            }));

            cases.Add(Case("runner_markers_recognized", "xUnit, NUnit, TAP, Jest, go, pytest, cargo, Touchstone and compiler markers are recognized", TestTags.Positive, () =>
            {
                string output =
                    "    Armada.Tests.Foo.Bar [FAIL]\n" +
                    "Failed : Armada.Tests.Nunit.Case\n" +
                    "not ok 3 - parses headers\n" +
                    " FAIL  src/app.test.ts\n" +
                    "--- FAIL: TestParse (0.00s)\n" +
                    "FAILED tests/test_x.py::test_y - AssertionError\n" +
                    "test parser::roundtrip ... FAILED\n" +
                    "Seeds prompt templates once                                     FAIL       152ms\n" +
                    "src/A.cs(10,5): error CS0103: The name 'x' does not exist\n" +
                    "ok 4 - fine\n" +
                    "PASS src/ok.test.ts\n";
                int total;
                List<string> lines = StageCheckOutputSummarizer.ExtractFailingLines(output, 50, out total);
                AssertEqual(9, lines.Count, "every failure marker and nothing else: " + String.Join(" | ", lines));
                AssertEqual(9, total);
            }));

            cases.Add(Case("touchstone_failed_tests_block", "The Touchstone console runner's Failed Tests block is kept with its assertion messages", TestTags.Positive, () =>
            {
                string output =
                    "Total: 31  Passed: 30  Failed: 1  Skipped: 0\n" +
                    "Failed Tests:\n" +
                    "  A Harbor returns NUL-terminated git output\n" +
                    "    Assertion failed: Expected a git result to be sent. was null\n" +
                    "\n" +
                    "done\n";
                int total;
                List<string> lines = StageCheckOutputSummarizer.ExtractFailingLines(output, 50, out total);
                AssertEqual(3, lines.Count, String.Join(" | ", lines));
                AssertEqual("  A Harbor returns NUL-terminated git output", lines[1]);
            }));

            cases.Add(Case("failing_lines_capped_and_deduplicated", "The failing section is capped and repeated lines are listed once", TestTags.Negative, () =>
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < 80; i++) builder.Append("  Failed Suite.Case").Append(i).Append(" [1 ms]\n");
                builder.Append("src/A.cs(1,1): error CS1002: ; expected\n");
                builder.Append("src/A.cs(1,1): error CS1002: ; expected\n");
                builder.Append(Noise("filler", 300));
                int total;
                List<string> lines = StageCheckOutputSummarizer.ExtractFailingLines(builder.ToString(), 50, out total);
                AssertEqual(50, lines.Count, "capped at 50");
                AssertEqual(81, total, "80 test lines plus one distinct compiler error");
                string summary = StageCheckOutputSummarizer.Summarize(builder.ToString());
                AssertContains("(31 more failure lines not shown)", summary);
            }));

            cases.Add(Case("negative_budget_throws", "A negative budget is rejected", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentOutOfRangeException>(() => StageCheckOutputSummarizer.Summarize("x", -1));
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "Stage Check Output Summarizer",
                cases: cases);
        }

        private static string Noise(string prefix, int count)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < count; i++) builder.Append(prefix).Append(' ').Append(i).Append(" lorem ipsum dolor sit amet\n");
            return builder.ToString();
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }
    }
}
