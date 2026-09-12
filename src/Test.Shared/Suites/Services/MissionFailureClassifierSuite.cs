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
    /// Descriptors for <see cref="MissionFailureClassifier"/>. Positive cases confirm mechanical failures
    /// (compile, tests, landing conflicts, timeouts, crashes) classify and are recoverable; negative cases
    /// confirm human-judgment failures (boundary, scope, judge, no-op, infra, unknown) are not auto-rescued.
    /// </summary>
    public sealed class MissionFailureClassifierSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the mission-failure-classifier suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("compile_is_recoverable", "A Definition-of-Done compile failure is recoverable Compile", TestTags.Positive, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "definition_of_done_compile: build phase exited 1");
                AssertEqual(MissionFailureKindEnum.Compile, kind);
                AssertTrue(MissionFailureClassifier.IsRecoverable(kind), "compile should be recoverable");
            }));

            cases.Add(Case("testfail_is_recoverable", "A Definition-of-Done test failure is recoverable TestFail", TestTags.Positive, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "definition_of_done_testfail: test phase exited 3");
                AssertEqual(MissionFailureKindEnum.TestFail, kind);
                AssertTrue(MissionFailureClassifier.IsRecoverable(kind), "testfail should be recoverable");
            }));

            cases.Add(Case("landing_failed_is_recoverable", "A LandingFailed status classifies as recoverable LandingConflict", TestTags.Positive, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.LandingFailed, "merge conflict while rebasing");
                AssertEqual(MissionFailureKindEnum.LandingConflict, kind);
                AssertTrue(MissionFailureClassifier.IsRecoverable(kind), "landing conflict should be recoverable");
            }));

            cases.Add(Case("crash_is_recoverable", "A provider usage-limit crash classifies as recoverable Crash", TestTags.Positive, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "captain exited: usage limit reached");
                AssertEqual(MissionFailureKindEnum.Crash, kind);
                AssertTrue(MissionFailureClassifier.IsRecoverable(kind), "crash should be recoverable");
            }));

            cases.Add(Case("no_op_is_not_recoverable", "A no-op completion is classified NoOp and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "no_op_completion_detected: captain repeatedly completed with no changes");
                AssertEqual(MissionFailureKindEnum.NoOp, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "no-op should not be auto-rescued");
            }));

            cases.Add(Case("judge_is_not_recoverable", "A Judge block is classified JudgeRejected and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "Judge verdict: FAIL");
                AssertEqual(MissionFailureKindEnum.JudgeRejected, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "judge rejection should not be auto-rescued");
            }));

            cases.Add(Case("scope_is_not_recoverable", "A scope violation is classified ScopeViolation and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "Mission modified files outside its scoped file list: a.cs");
                AssertEqual(MissionFailureKindEnum.ScopeViolation, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "scope violation should not be auto-rescued");
            }));

            cases.Add(Case("boundary_is_not_recoverable", "A boundary finding is classified Boundary and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "Blocked: a secret was detected in the diff");
                AssertEqual(MissionFailureKindEnum.Boundary, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "boundary finding should not be auto-rescued");
            }));

            cases.Add(Case("infra_is_not_recoverable", "A Definition-of-Done infra failure is classified Infra and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, "definition_of_done_infra: build command failed to start");
                AssertEqual(MissionFailureKindEnum.Infra, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "infra failure should not be auto-rescued");
            }));

            cases.Add(Case("empty_reason_is_unknown", "An empty failure reason is Unknown and not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(MissionStatusEnum.Failed, null);
                AssertEqual(MissionFailureKindEnum.Unknown, kind);
                AssertFalse(MissionFailureClassifier.IsRecoverable(kind), "unknown should not be auto-rescued");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionFailureClassifier",
                displayName: "Mission Failure Classifier",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionFailureClassifier",
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
