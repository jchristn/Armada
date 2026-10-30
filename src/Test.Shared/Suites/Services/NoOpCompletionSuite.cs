namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="NoOpCompletionDetector"/>. Positive cases confirm a fast, empty-diff,
    /// trivial-output completion is flagged, and that a quoted, fenced, or extended completion marker
    /// leaves an empty same-commit stage unfinished. Negative cases confirm a real diff, a slow run, an
    /// unknown duration, verbose output, and a standalone completion line stay clear of the check that
    /// case calls.
    /// </summary>
    public sealed class NoOpCompletionSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the no-op completion suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("noop_empty_diff_fast_trivial_is_flagged", "Empty diff + fast + trivial output is flagged", TestTags.Positive, () =>
            {
                AssertTrue(NoOpCompletionDetector.IsNoOp("", "Done.", 1200));
                AssertTrue(NoOpCompletionDetector.IsNoOp(null, "   ", 500));
                AssertTrue(NoOpCompletionDetector.IsNoOp("   \n  ", "[ARMADA:PROGRESS] 100", 3000));
            }));

            cases.Add(Case("noop_nonempty_diff_not_flagged", "Non-empty diff is never flagged", TestTags.Negative, () =>
            {
                AssertFalse(NoOpCompletionDetector.IsNoOp("diff --git a/x b/x\n+change", "Done.", 1000));
            }));

            cases.Add(Case("noop_slow_run_not_flagged", "A slow run is not a fast false-complete", TestTags.Negative, () =>
            {
                // Empty diff but the captain spent real time investigating -> legitimate "nothing to do".
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "Investigated; no change needed.", 120000));
            }));

            cases.Add(Case("noop_unknown_runtime_not_flagged", "Unknown runtime is not flagged (conservative)", TestTags.Negative, () =>
            {
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "Done.", null));
            }));

            cases.Add(Case("noop_verbose_output_not_flagged", "Verbose output is not trivial", TestTags.Negative, () =>
            {
                string verbose = new string('x', 1000);
                AssertFalse(NoOpCompletionDetector.IsNoOp("", verbose, 2000));
            }));

            cases.Add(Case("noop_custom_thresholds_respected", "Custom thresholds are honored", TestTags.Positive, () =>
            {
                // With a 10s cap, a 5s run is fast; with a 2s cap, the same run is not.
                AssertTrue(NoOpCompletionDetector.IsNoOp("", "ok", 5000, maxRuntimeMs: 10000));
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "ok", 5000, maxRuntimeMs: 2000));
            }));

            cases.Add(Case("unfinished_quoted_marker_stays_unfinished", "A quoted completion marker with an empty diff and the same commit is unfinished", TestTags.Positive, () =>
            {
                string commit = "abc111";
                string output = "The notes quote [ARMADA:RESULT] COMPLETE in the middle of a sentence.";
                AssertTrue(
                    NoOpCompletionDetector.IsUnfinishedWork("", output, commit, commit),
                    "quoted marker stays unfinished");
            }));

            cases.Add(Case("unfinished_fenced_marker_stays_unfinished", "A completion marker inside a fence with an empty diff and the same commit is unfinished", TestTags.Positive, () =>
            {
                string commit = "abc111";
                string output = "The reply is below.\n```\n[ARMADA:RESULT] COMPLETE\n```\n";
                AssertTrue(
                    NoOpCompletionDetector.IsUnfinishedWork("", output, commit, commit),
                    "fenced marker stays unfinished");
            }));

            cases.Add(Case("unfinished_marker_with_extra_words_stays_unfinished", "A completion marker with more words on its line and an empty diff on the same commit is unfinished", TestTags.Positive, () =>
            {
                string commit = "abc111";
                string output = "[ARMADA:RESULT] COMPLETE and a summary on the same line";
                AssertTrue(
                    NoOpCompletionDetector.IsUnfinishedWork("", output, commit, commit),
                    "marker with extra words stays unfinished");
            }));

            cases.Add(Case("unfinished_standalone_completion_line_counts", "A standalone completion line with an empty diff and the same commit is finished", TestTags.Negative, () =>
            {
                string commit = "abc111";
                string output = "\n  [ARMADA:RESULT] COMPLETE  \n";
                AssertFalse(
                    NoOpCompletionDetector.IsUnfinishedWork("", output, commit, commit),
                    "standalone completion line counts as finished");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.NoOpCompletion",
                displayName: "No-Op Completion Detector",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.NoOpCompletion",
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
