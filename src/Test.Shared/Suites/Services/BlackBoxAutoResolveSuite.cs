namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box entries of a mission that later completes are resolved by the system (BlackBoxCompletionResolver), and
    /// recovery paths that target a mission no longer in a failed state are shown as not applicable.
    /// </summary>
    public sealed class BlackBoxAutoResolveSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.BlackBoxAutoResolve";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("unconfirmed_stop_resolved_when_mission_completes", "An open UnconfirmedStop entry is resolved by the system when its mission completes, with the completion time and commit, once", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "Recovery deferred: the previous job has not confirmed its exit.", MissionStatusEnum.InProgress).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);

                AssertEqual(0, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false), "nothing to resolve before the mission completes");
                DateTime completed = await CompleteAsync(t, mission, "abc1234", "mission.completed", null).ConfigureAwait(false);

                AssertEqual(1, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false), "the open entry is resolved");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxResolutionEnum.Resolved, entry.Resolution);
                AssertEqual("Mission completed after this failure at " + completed.ToString("u") + "; commit abc1234.", entry.ResolutionNote);
                AssertNotNull(entry.ResolvedUtc, "resolution time");
                AssertEqual(BlackBoxRetentionPolicy.ComputeExpiry(entry, settings.BlackBox.Retention, null), entry.ExpiresUtc, "expiry recomputed for the resolved entry");
                EnumerationResult<BlackBoxNote> notes = await t.Driver.BlackBoxNotes.EnumerateAsync(id, new EnumerationQuery()).ConfigureAwait(false);
                AssertTrue(notes.Objects.Any(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.System && n.Body == entry.ResolutionNote), "a System note records why");
                AssertEqual(0, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false), "an event is handled once");
            }));

            cases.Add(Case("landing_failed_reconciled_resolved_with_landing_commit", "A LandingFailed entry is resolved when the landing is reconciled to Complete, naming the commit the work landed as", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "Landing refused: dirty checkout", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout).ConfigureAwait(false))!;
                LandingReconciliationEvidence evidence = new LandingReconciliationEvidence { MissionId = mission.Id, EvidenceCommit = "fff9999", TargetRef = "origin/main" };
                await CompleteAsync(t, mission, "abc1234", "mission.landing_reconciled", JsonSerializer.Serialize(evidence)).ConfigureAwait(false);

                AssertEqual(1, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false));
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxResolutionEnum.Resolved, entry.Resolution);
                AssertContains("; commit abc1234; landed as fff9999.", entry.ResolutionNote ?? "");
            }));

            cases.Add(Case("operator_resolution_is_kept", "An entry the operator already resolved is not changed when the mission completes", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Service.ResolveAsync(BlackBoxTestKit.Admin(), id, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.WontFix, Note = "operator decision" }).ConfigureAwait(false);
                await CompleteAsync(t, mission, "abc1234", "mission.completed", null).ConfigureAwait(false);

                AssertEqual(0, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false), "the operator's resolution stands");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxResolutionEnum.WontFix, entry.Resolution);
                AssertEqual("operator decision", entry.ResolutionNote);
            }));

            cases.Add(Case("pinned_entry_is_resolved_and_stays_pinned", "A pinned open entry is resolved when the mission completes and stays pinned", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "stop not confirmed", MissionStatusEnum.InProgress).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await bb.Service.PinAsync(BlackBoxTestKit.Admin(), id, true).ConfigureAwait(false);
                await CompleteAsync(t, mission, null, "mission.completed", null).ConfigureAwait(false);

                AssertEqual(1, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false));
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxResolutionEnum.Resolved, entry.Resolution);
                AssertTrue(entry.Pinned, "still pinned");
                AssertContains("commit unknown", entry.ResolutionNote ?? "", "a mission without a recorded commit says so");
            }));

            cases.Add(Case("mission_not_complete_is_not_resolved", "A completion event for a mission that is not Complete resolves nothing", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "stop not confirmed", MissionStatusEnum.InProgress).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await t.Driver.Events.CreateAsync(new ArmadaEvent("mission.completed", "Mission completed") { MissionId = mission.Id, EntityType = "mission", EntityId = mission.Id }).ConfigureAwait(false);

                AssertEqual(0, await bb.Insights.Completions.RunOnceAsync().ConfigureAwait(false));
                AssertEqual(BlackBoxResolutionEnum.Open, (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!.Resolution);
            }));

            cases.Add(Case("insight_pass_runs_the_resolver", "The insight worker's pass resolves entries of completed missions", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "stop not confirmed", MissionStatusEnum.InProgress).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await CompleteAsync(t, mission, "abc1234", "mission.completed", null).ConfigureAwait(false);

                await bb.Insights.Background.RunOnceAsync().ConfigureAwait(false);
                AssertEqual(BlackBoxResolutionEnum.Resolved, (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!.Resolution);
            }));

            cases.Add(Case("restart_not_applicable_once_complete", "After the mission completes, the brief and the analysis show the restart path as not applicable instead of recommending it", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Infra, "stop not confirmed", MissionStatusEnum.InProgress).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);
                await CompleteAsync(t, mission, "abc1234", "mission.completed", null).ConfigureAwait(false);

                BlackBoxEntry entry = await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                BlackBoxRecoveryPath restart = entry.Record!.Analysis!.RecoveryPaths.First(p => p.Steps.Any(s => s.Action?.Tool == "restart_mission"));
                AssertEqual("Not applicable: the mission is Complete.", restart.NotApplicableReason);
                AssertFalse((entry.Record.Analysis.Summary ?? "").Contains("Suggested next step: restart", StringComparison.Ordinal), "the summary no longer recommends a restart");
                AssertContains("Suggested next step: none; not applicable: the mission is Complete.", entry.Record.Analysis.Summary ?? "");
                string brief = await bb.Service.GetBriefAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                AssertContains("Not applicable: the mission is Complete.", brief);
                AssertFalse(brief.Contains("Action: `restart_mission", StringComparison.Ordinal), "the brief does not offer the restart action");
            }));

            cases.Add(Case("restart_applicable_while_failed", "While the mission is Failed the restart path applies and the brief offers it", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.StallRecoveryExhausted, "Captain stalled, recovery exhausted.", MissionStatusEnum.Failed).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false))!;
                await bb.Pipeline.ProcessAsync(id).ConfigureAwait(false);

                BlackBoxEntry entry = await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                BlackBoxRecoveryPath restart = entry.Record!.Analysis!.RecoveryPaths.First(p => p.Steps.Any(s => s.Action?.Tool == "restart_mission"));
                AssertNull(restart.NotApplicableReason, "a Failed mission can be restarted");
                string brief = await bb.Service.GetBriefAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                AssertContains("Action: `restart_mission", brief);
                AssertFalse(brief.Contains("Not applicable:", StringComparison.Ordinal));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black Box Auto-Resolution", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<DateTime> CompleteAsync(TestDatabase t, Mission mission, string? commit, string eventType, string? payload)
        {
            Mission current = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
            current.Status = MissionStatusEnum.Complete;
            current.CommitHash = commit;
            current.CompletedUtc = DateTime.UtcNow.AddSeconds(1);
            current.FailureReason = null;
            current.FailureKind = null;
            await t.Driver.Missions.UpdateAsync(current).ConfigureAwait(false);
            await t.Driver.Events.CreateAsync(new ArmadaEvent(eventType, "Mission completed") { MissionId = mission.Id, EntityType = "mission", EntityId = mission.Id, Payload = payload }).ConfigureAwait(false);
            return (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.CompletedUtc!.Value;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        #endregion
    }
}
