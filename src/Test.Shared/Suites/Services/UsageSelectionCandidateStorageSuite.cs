namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>A decision stores the candidates that explain it and counts the rest by owner and reason.</summary>
    public sealed class UsageSelectionCandidateStorageSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.UsageSelectionCandidateStorage";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("admission_stores_selected_and_counts_others", "An Enforce admission over 1000 captains stores the selected captain and counts the other 999 by reason", () =>
                {
                    UsageSelectionSnapshot snapshot = Snapshot(UsageSelectionModeEnum.Enforce, 1000, "cpt_0007", "cpt_0007");
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, Mission(null), "Dispatch", admitted: true);
                    AssertEqual(1, records.Candidates.Count); AssertEqual("cpt_0007", records.Candidates[0].CaptainId);
                    AssertEqual(8, records.Candidates[0].Rank, "the stored row keeps its rank among every evaluated candidate");
                    AssertEqual(1000, records.Decision.CandidateCount);
                    AssertEqual(999, records.OmittedCandidates.Sum(o => o.CandidateCount));
                    AssertEqual(499, records.OmittedCandidates.Single(o => o.ReasonCode == UsageSelectionReasonCodes.BelowReserve).CandidateCount);
                    AssertEqual(500, records.OmittedCandidates.Single(o => o.ReasonCode == UsageSelectionReasonCodes.UsageStale).CandidateCount);
                    AssertTrue(records.Windows.All(w => w.CandidateId == records.Candidates[0].Id), "only stored candidates carry window evidence");
                    AssertTrue(records.Reasons.Count == 1);
                }),
                Case("observe_stores_actual_and_proposed", "Observe stores the captain that ran and the one the policy proposed", () =>
                {
                    UsageSelectionSnapshot snapshot = Snapshot(UsageSelectionModeEnum.Observe, 50, "cpt_0040", "cpt_0001");
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, Mission(null), "Dispatch", admitted: true);
                    AssertEqual("cpt_0001,cpt_0040", String.Join(",", records.Candidates.Select(c => c.CaptainId).OrderBy(c => c, StringComparer.Ordinal)));
                    AssertEqual(48, records.OmittedCandidates.Sum(o => o.CandidateCount));
                }),
                Case("wait_stores_nearest_candidates_and_requested", "A wait stores the 25 nearest candidates plus the requested captain and counts the rest", () =>
                {
                    UsageSelectionSnapshot snapshot = Snapshot(UsageSelectionModeEnum.Enforce, 100, null, null);
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, Mission("cpt_0090"), "Waiting", admitted: false);
                    AssertEqual(UsageSelectionRecordBuilder.MaximumStoredWaitingCandidates + 1, records.Candidates.Count);
                    AssertEqual(String.Join(",", Enumerable.Range(1, 25).Concat(new[] { 91 })), String.Join(",", records.Candidates.Select(c => c.Rank)));
                    AssertEqual(74, records.OmittedCandidates.Sum(o => o.CandidateCount));
                    AssertEqual(100, records.Decision.CandidateCount);
                }),
                Case("observe_proposed_wait_stores_nearest_candidates", "Observe whose policy would wait stores the nearest candidates that explain the proposed wait", () =>
                {
                    UsageSelectionSnapshot snapshot = Snapshot(UsageSelectionModeEnum.Observe, 40, "cpt_0039", null);
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, Mission(null), "Dispatch", admitted: true);
                    AssertEqual(26, records.Candidates.Count); AssertTrue(records.Candidates.Any(c => c.CaptainId == "cpt_0039"));
                }),
                Case("small_wait_stores_every_candidate", "A wait over ten captains stores every candidate and omits none", () =>
                {
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(Snapshot(UsageSelectionModeEnum.Enforce, 10, null, null), Mission(null), "Waiting", admitted: false);
                    AssertEqual(10, records.Candidates.Count); AssertEqual(0, records.OmittedCandidates.Count);
                }),
                Case("omitted_counts_are_split_by_owner", "Omitted counts are kept per captain owner so scoped readers never count hidden peers", () =>
                {
                    UsageSelectionSnapshot snapshot = Snapshot(UsageSelectionModeEnum.Enforce, 10, "cpt_0000", "cpt_0000");
                    foreach (Captain captain in snapshot.Captains.Values.Where(c => c.Id != "cpt_0000")) captain.UserId = Int32.Parse(captain.Id.Substring(4)) % 2 == 0 ? "usr_a" : "usr_b";
                    CaptainSelectionRecordSet records = UsageSelectionRecordBuilder.Build(snapshot, Mission(null), "Dispatch", admitted: true);
                    AssertEqual(4, records.OmittedCandidates.Where(o => o.UserId == "usr_a").Sum(o => o.CandidateCount));
                    AssertEqual(5, records.OmittedCandidates.Where(o => o.UserId == "usr_b").Sum(o => o.CandidateCount));
                })
            };
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Captain selection candidate storage", cases: cases);
        }

        // Candidates are already in rank order. Odd ranks are below reserve, even ranks stale, except the selectable ones.
        private static UsageSelectionSnapshot Snapshot(UsageSelectionModeEnum mode, int count, string? actual, string? proposed)
        {
            UsageSelectionSnapshot snapshot = new UsageSelectionSnapshot
            {
                Settings = new UsageSelectionSettings { Mode = mode }, ActualCaptainId = actual, EvaluatedUtc = DateTime.UtcNow,
                Captains = new Dictionary<string, Captain>(StringComparer.Ordinal)
            };
            for (int index = 0; index < count; index++)
            {
                string id = "cpt_" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                snapshot.Captains[id] = new Captain("captain " + index) { Id = id, TenantId = "ten", UserId = "usr", Model = "opus" };
                bool selectable = id == actual || id == proposed;
                UsageSelectionCandidate candidate = new UsageSelectionCandidate { CaptainId = id, Model = "opus" };
                candidate.Windows.Add(new UsageSelectionWindowEvidence { Window = new UsageWindow { Name = "five_hour", UsedPercent = 40 }, ScopeApplies = true, WindowIdentityConfirmed = true });
                snapshot.Evaluation.Candidates.Add(new UsageSelectionCandidateResult
                {
                    Candidate = candidate, CanSelect = selectable,
                    ReasonCode = selectable ? UsageSelectionReasonCodes.SufficientHeadroom : index % 2 == 0 ? UsageSelectionReasonCodes.UsageStale : UsageSelectionReasonCodes.BelowReserve
                });
            }
            snapshot.Evaluation.SelectedCaptainId = proposed;
            snapshot.Evaluation.ReasonCode = proposed == null ? UsageSelectionReasonCodes.BelowReserve : UsageSelectionReasonCodes.SelectedForHeadroom;
            return snapshot;
        }

        private static Mission Mission(string? requested) => new Mission("storage") { TenantId = "ten", UserId = "usr", RequestedCaptainId = requested };

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
            => new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: token => { body(); return Task.CompletedTask; }, tags: new List<string> { TestTags.Positive });
    }
}
