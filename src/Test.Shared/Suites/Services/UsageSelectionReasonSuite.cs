namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>An admitted decision's reason says what actually chose the captain.</summary>
    public sealed class UsageSelectionReasonSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.UsageSelectionReason";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("observe_admission_is_routing", "Observe admits the routing choice, never claiming the strategy's headroom",
                    () => AssertEqual(UsageSelectionReasonCodes.SelectedByRouting, Reason(UsageSelectionModeEnum.Observe, UsageSelectionReasonCodes.BelowReserve, null, null))),
                Case("enforce_ranking_is_headroom", "Enforce with an ordinary ranked choice keeps SelectedForHeadroom",
                    () => AssertEqual(UsageSelectionReasonCodes.SelectedForHeadroom, Reason(UsageSelectionModeEnum.Enforce, UsageSelectionReasonCodes.SufficientHeadroom, null, null))),
                Case("enforce_reserve_exception", "A captain admitted through the soft reserve names the authorized exception",
                    () => AssertEqual(UsageSelectionReasonCodes.AuthorizedReserveException, Reason(UsageSelectionModeEnum.Enforce, UsageSelectionReasonCodes.AuthorizedReserveException, Recovery(false, true), null))),
                Case("enforce_fallback", "A captain other than the requested one, under fallback permission, names the authorized fallback",
                    () => AssertEqual(UsageSelectionReasonCodes.AuthorizedFallback, Reason(UsageSelectionModeEnum.Enforce, UsageSelectionReasonCodes.SufficientHeadroom, Recovery(true, false), "cpt_preferred"))),
                Case("enforce_requested_captain_is_not_fallback", "The requested captain itself is not a fallback even when fallback is allowed",
                    () => AssertEqual(UsageSelectionReasonCodes.SelectedForHeadroom, Reason(UsageSelectionModeEnum.Enforce, UsageSelectionReasonCodes.SufficientHeadroom, Recovery(true, false), "cpt_chosen")))
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Captain selection admitted reasons", cases: cases);
        }

        private static string Reason(UsageSelectionModeEnum mode, string candidateReason, UsageSelectionRecovery? recovery, string? requested)
        {
            Captain chosen = new Captain("chosen") { Id = "cpt_chosen", TenantId = "ten", UserId = "usr", Model = "opus" };
            UsageSelectionSnapshot snapshot = new UsageSelectionSnapshot
            {
                Settings = new UsageSelectionSettings { Mode = mode }, Recovery = recovery, ActualCaptainId = chosen.Id, EvaluatedUtc = DateTime.UtcNow,
                Captains = new Dictionary<string, Captain>(StringComparer.Ordinal) { [chosen.Id] = chosen }
            };
            snapshot.Evaluation.SelectedCaptainId = chosen.Id;
            snapshot.Evaluation.ReasonCode = UsageSelectionReasonCodes.SelectedForHeadroom;
            snapshot.Evaluation.Candidates.Add(new UsageSelectionCandidateResult { Candidate = new UsageSelectionCandidate { CaptainId = chosen.Id }, ReasonCode = candidateReason, CanSelect = true });
            Mission mission = new Mission("reason") { TenantId = "ten", UserId = "usr", RequestedCaptainId = requested };
            return UsageSelectionRecordBuilder.Build(snapshot, mission, "Dispatch", admitted: true).Decision.PrimaryReasonCode;
        }

        private static UsageSelectionRecovery Recovery(bool fallback, bool reserve)
            => new UsageSelectionRecovery { Id = "ura_1", MissionId = "msn", ActorUserId = "usr_admin", Reason = "test", AllowFallback = fallback, ReserveException = reserve, Revision = 1, CreatedUtc = DateTime.UtcNow };

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => { body(); return Task.CompletedTask; }, tags: new List<string> { TestTags.Positive });
    }
}
