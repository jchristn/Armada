namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="FailureKindMapper"/>. Positive cases confirm the persisted failure kind
    /// decides and mechanical kinds (compile, tests, landing conflicts, timeouts, crashes) are recoverable; negative
    /// cases confirm human-judgment kinds are not auto-rescued and that failure reason text is never read.
    /// </summary>
    public sealed class FailureKindMapperSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the failure-kind-mapper suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("recorded_kind_decides", "The persisted failure kind is returned as recorded", TestTags.Positive, () =>
            {
                foreach (MissionFailureKindEnum recorded in Enum.GetValues<MissionFailureKindEnum>())
                {
                    AssertEqual(recorded, FailureKindMapper.FromStatus(MissionStatusEnum.Failed, recorded), "kind " + recorded + " round-trips");
                }
            }));

            cases.Add(Case("mechanical_kinds_are_recoverable", "Compile, TestFail, LandingConflict, Timeout, and Crash are recoverable", TestTags.Positive, () =>
            {
                AssertTrue(FailureKindMapper.IsRecoverable(MissionFailureKindEnum.Compile), "compile should be recoverable");
                AssertTrue(FailureKindMapper.IsRecoverable(MissionFailureKindEnum.TestFail), "testfail should be recoverable");
                AssertTrue(FailureKindMapper.IsRecoverable(MissionFailureKindEnum.LandingConflict), "landing conflict should be recoverable");
                AssertTrue(FailureKindMapper.IsRecoverable(MissionFailureKindEnum.Timeout), "timeout should be recoverable");
                AssertTrue(FailureKindMapper.IsRecoverable(MissionFailureKindEnum.Crash), "crash should be recoverable");
            }));

            cases.Add(Case("judgment_kinds_are_not_recoverable", "Kinds that need a human are not auto-rescued", TestTags.Negative, () =>
            {
                MissionFailureKindEnum[] humanKinds = new MissionFailureKindEnum[]
                {
                    MissionFailureKindEnum.NoOp,
                    MissionFailureKindEnum.Boundary,
                    MissionFailureKindEnum.ScopeViolation,
                    MissionFailureKindEnum.JudgeRejected,
                    MissionFailureKindEnum.Infra,
                    MissionFailureKindEnum.Unknown,
                    MissionFailureKindEnum.ReviewDenied,
                    MissionFailureKindEnum.DependencyFailed,
                    MissionFailureKindEnum.MaxRuntimeExceeded,
                    MissionFailureKindEnum.StallRecoveryExhausted,
                    MissionFailureKindEnum.OperatorAction,
                    MissionFailureKindEnum.InvalidOutput,
                    MissionFailureKindEnum.Blocked
                };
                foreach (MissionFailureKindEnum kind in humanKinds)
                {
                    AssertFalse(FailureKindMapper.IsRecoverable(kind), kind + " should not be auto-rescued");
                }
            }));

            cases.Add(Case("landing_failed_without_kind_is_landing_conflict", "A LandingFailed mission without a recorded kind is a landing conflict", TestTags.Positive, () =>
            {
                AssertEqual(MissionFailureKindEnum.LandingConflict, FailureKindMapper.FromStatus(MissionStatusEnum.LandingFailed, null));
            }));

            cases.Add(Case("failed_without_kind_is_unknown", "A Failed mission without a recorded kind is Unknown and not recoverable", TestTags.Negative, () =>
            {
                MissionFailureKindEnum kind = FailureKindMapper.FromStatus(MissionStatusEnum.Failed, null);
                AssertEqual(MissionFailureKindEnum.Unknown, kind);
                AssertFalse(FailureKindMapper.IsRecoverable(kind), "unknown should not be auto-rescued");
            }));

            cases.Add(Case("reason_text_is_never_read", "Reason text that names other failures does not change the recorded kind", TestTags.Negative, () =>
            {
                // Before the fix a judge rejection whose reason quoted a compile error, or a scope violation listing
                // BillingService.cs, was reclassified from the text. The recorded kind now decides.
                Mission judged = new Mission("judge", "review");
                judged.Status = MissionStatusEnum.Failed;
                judged.FailureKind = MissionFailureKindEnum.JudgeRejected;
                judged.FailureReason = "definition_of_done_compile: merge conflict crash timeout";
                AssertEqual(MissionFailureKindEnum.JudgeRejected, FailureKindMapper.FromMission(judged));

                Mission untagged = new Mission("legacy", "legacy");
                untagged.Status = MissionStatusEnum.Failed;
                untagged.FailureReason = "definition_of_done_compile: build phase exited 1";
                AssertEqual(MissionFailureKindEnum.Unknown, FailureKindMapper.FromMission(untagged), "text alone never classifies");
            }));

            cases.Add(Case("definition_of_done_outcomes_map", "Definition-of-Done outcomes map to their failure kinds", TestTags.Positive, () =>
            {
                AssertEqual(MissionFailureKindEnum.Compile, FailureKindMapper.FromDefinitionOfDone(DefinitionOfDoneOutcomeEnum.Compile));
                AssertEqual(MissionFailureKindEnum.TestFail, FailureKindMapper.FromDefinitionOfDone(DefinitionOfDoneOutcomeEnum.TestFail));
                AssertEqual(MissionFailureKindEnum.Timeout, FailureKindMapper.FromDefinitionOfDone(DefinitionOfDoneOutcomeEnum.Timeout));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromDefinitionOfDone(DefinitionOfDoneOutcomeEnum.Infra));
            }));

            cases.Add(Case("runtime_exits_map", "Runtime exits map to one mission failure kind", TestTags.Positive, () =>
            {
                AssertEqual(MissionFailureKindEnum.InvalidOutput, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.TaskFailure, 1));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.Transient, 1), "an outlasted outage is the environment");
                AssertEqual(MissionFailureKindEnum.Crash, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.Crash, 137));
                AssertEqual(MissionFailureKindEnum.Crash, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.Crash, null));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.Crash, -1), "a negative exit is Armada stopping or losing the process");
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromRuntimeExit(RuntimeFailureKindEnum.TaskFailure, -1));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromLostLandingHandoff());
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.FailureKindMapper",
                displayName: "Failure Kind Mapper",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.FailureKindMapper",
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
