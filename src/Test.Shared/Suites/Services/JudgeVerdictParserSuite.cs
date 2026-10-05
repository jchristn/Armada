namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="JudgeVerdictParser"/>. Positive cases confirm the whole-line [ARMADA:VERDICT]
    /// protocol line is read; negative cases confirm prose verdicts, bare PASS/FAIL lines from test runners, inline
    /// mentions, fenced examples, and conflicting verdict lines produce no verdict.
    /// </summary>
    public sealed class JudgeVerdictParserSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("protocol_line_is_read", "The [ARMADA:VERDICT] protocol line is the verdict", TestTags.Positive, () =>
            {
                AssertEqual(JudgeVerdictEnum.Pass, JudgeVerdictParser.Parse("## Verdict\n[ARMADA:VERDICT] PASS\n"));
                AssertEqual(JudgeVerdictEnum.Fail, JudgeVerdictParser.Parse("review...\r\n  [ARMADA:VERDICT] FAIL  \r\nthanks"));
                AssertEqual(JudgeVerdictEnum.NeedsRevision, JudgeVerdictParser.Parse("[ARMADA:VERDICT] NEEDS_REVISION"));
            }));

            cases.Add(Case("repeated_identical_line_is_accepted", "A repeated identical verdict line is accepted", TestTags.Positive, () =>
            {
                AssertEqual(JudgeVerdictEnum.Fail, JudgeVerdictParser.Parse("[ARMADA:VERDICT] FAIL\nsummary\n[ARMADA:VERDICT] FAIL"));
            }));

            cases.Add(Case("trailing_test_runner_pass_does_not_override", "A test runner PASS line after a FAIL verdict does not flip it", TestTags.Negative, () =>
            {
                // The old bottom-up regex scan returned the last bare PASS/FAIL line, so a trailing test-runner line
                // decided landing.
                string output =
                    "## Correctness\nThe change breaks the empty-list path.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n\n" +
                    "Test run output:\n" +
                    "PASS\n" +
                    "Verdict: PASS\n" +
                    "### Verdict: **PASS**\n";
                AssertEqual(JudgeVerdictEnum.Fail, JudgeVerdictParser.Parse(output));
            }));

            cases.Add(Case("prose_verdicts_are_not_verdicts", "Prose, headings, bare words, and inline mentions are not verdicts", TestTags.Negative, () =>
            {
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("### Verdict: **PASS**\nThe mission is complete."));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("My verdict is PASS because everything looks correct."));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("PASS"));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("FAIL: 3 tests failed"));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("- `[ARMADA:VERDICT] PASS` -- judge approves the mission"));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("I would emit [ARMADA:VERDICT] PASS here"));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("[ARMADA:VERDICT] APPROVED"));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse(null));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse(""));
            }));

            cases.Add(Case("fenced_examples_are_ignored", "Verdict lines inside fenced code blocks are ignored", TestTags.Negative, () =>
            {
                string output = "Example format:\n```\n[ARMADA:VERDICT] PASS\n```\n[ARMADA:VERDICT] NEEDS_REVISION\n";
                AssertEqual(JudgeVerdictEnum.NeedsRevision, JudgeVerdictParser.Parse(output));
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("```text\n[ARMADA:VERDICT] PASS\n```"));
            }));

            cases.Add(Case("conflicting_lines_are_no_verdict", "Conflicting verdict lines are treated as no verdict", TestTags.Negative, () =>
            {
                AssertEqual(JudgeVerdictEnum.None, JudgeVerdictParser.Parse("[ARMADA:VERDICT] FAIL\nlater\n[ARMADA:VERDICT] PASS"));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.JudgeVerdictParser",
                displayName: "Judge Verdict Parser",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.JudgeVerdictParser",
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
