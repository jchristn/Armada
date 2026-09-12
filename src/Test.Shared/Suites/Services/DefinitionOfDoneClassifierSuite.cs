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
    /// Descriptors for <see cref="DefinitionOfDoneClassifier"/>. Positive cases confirm a clean exit is Pass;
    /// negative cases confirm a failed start is Infra, a killed phase is Timeout, and a non-zero exit is
    /// Compile for the build phase and TestFail for the test phase.
    /// </summary>
    public sealed class DefinitionOfDoneClassifierSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Definition-of-Done classifier suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("build_clean_exit_is_pass", "A clean build exit is Pass", TestTags.Positive, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Pass, DefinitionOfDoneClassifier.ClassifyPhase(true, false, 0, true));
            }));

            cases.Add(Case("test_clean_exit_is_pass", "A clean test exit is Pass", TestTags.Positive, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Pass, DefinitionOfDoneClassifier.ClassifyPhase(true, false, 0, false));
            }));

            cases.Add(Case("build_nonzero_is_compile", "A non-zero build exit is Compile", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Compile, DefinitionOfDoneClassifier.ClassifyPhase(true, false, 1, true));
            }));

            cases.Add(Case("test_nonzero_is_testfail", "A non-zero test exit is TestFail", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.TestFail, DefinitionOfDoneClassifier.ClassifyPhase(true, false, 1, false));
            }));

            cases.Add(Case("build_timeout_is_timeout", "A killed build phase is Timeout", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Timeout, DefinitionOfDoneClassifier.ClassifyPhase(true, true, -1, true));
            }));

            cases.Add(Case("test_timeout_is_timeout", "A killed test phase is Timeout", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Timeout, DefinitionOfDoneClassifier.ClassifyPhase(true, true, -1, false));
            }));

            cases.Add(Case("not_started_is_infra", "A phase that never started is Infra", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Infra, DefinitionOfDoneClassifier.ClassifyPhase(false, false, 0, true));
                AssertEqual(DefinitionOfDoneOutcomeEnum.Infra, DefinitionOfDoneClassifier.ClassifyPhase(false, false, 0, false));
            }));

            cases.Add(Case("timeout_wins_over_exit_code", "A timeout classifies as Timeout even with a captured exit code", TestTags.Negative, () =>
            {
                AssertEqual(DefinitionOfDoneOutcomeEnum.Timeout, DefinitionOfDoneClassifier.ClassifyPhase(true, true, 1, true));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.DefinitionOfDoneClassifier",
                displayName: "Definition-of-Done Classifier",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.DefinitionOfDoneClassifier",
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
