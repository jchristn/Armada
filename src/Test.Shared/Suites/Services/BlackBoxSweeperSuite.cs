namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// BlackBoxSweeper and backfill: captures failed missions and voyages without an entry, never twice, infers the step from the kind, and requeues due EnrichmentFailed entries.
    /// </summary>
    public sealed class BlackBoxSweeperSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxSweeper";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("captures_missing_no_duplicates", "The sweeper captures each failed subject once with CaptureMode Sweeper; a second sweep captures nothing", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission a = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.NoOp, "no changes").ConfigureAwait(false);
                Mission b = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, null, "landing failed", MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.OperatorAction, "stopped", MissionStatusEnum.Cancelled).ConfigureAwait(false);
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("swept voyage") { Status = VoyageStatusEnum.Failed }).ConfigureAwait(false);
                BlackBoxBackfillResult first = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(3, first.Captured, "two missions and one voyage");
                AssertEqual(0, first.Failed, "no failures");
                BlackBoxEntry ea = (await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync(a.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxCaptureModeEnum.Sweeper, ea.CaptureMode); AssertEqual(BlackBoxStepEnum.NoOp, ea.Step, "step inferred from kind");
                AssertEqual(BlackBoxStepEnum.TargetIntegration, (await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync(b.Id).ConfigureAwait(false))!.Step, "an unclassified landing failure keeps the Landing phase");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadCurrentByVoyageAsync(voyage.Id).ConfigureAwait(false), "voyage captured");
                BlackBoxBackfillResult second = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(0, second.Captured, "nothing captured twice");
                AssertEqual(3L, await t.Driver.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest()).ConfigureAwait(false), "three entries");
                BlackBoxBackfillResult backfill = await bb.Sweeper.BackfillAsync(null, 100).ConfigureAwait(false);
                AssertEqual(0, backfill.Captured, "backfill is idempotent");
            }));

            cases.Add(Case("current_entry_skipped_and_requeue", "A mission with a current entry is skipped; a due EnrichmentFailed entry is requeued; backfill marks CaptureMode Backfill", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission inline = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(inline, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Queue.DrainAsync().ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdateStateAsync(id, BlackBoxStateEnum.EnrichmentFailed, "x", 1, DateTime.UtcNow.AddMinutes(-1)).ConfigureAwait(false);
                BlackBoxBackfillResult sweep = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(0, sweep.Captured, "inline-captured mission skipped");
                AssertEqual(1, sweep.EnrichmentRequeued, "requeued");
                Mission historical = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                BlackBoxBackfillResult backfill = await bb.Sweeper.BackfillAsync(DateTime.UtcNow.AddDays(-1), 10).ConfigureAwait(false);
                AssertEqual(1, backfill.Captured, "backfilled");
                AssertEqual(BlackBoxCaptureModeEnum.Backfill, (await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync(historical.Id).ConfigureAwait(false))!.CaptureMode);
                Mission later = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                AssertEqual(0, (await bb.Sweeper.BackfillAsync(DateTime.UtcNow.AddDays(1), 10).ConfigureAwait(false)).Captured, "since filter");
            }));

            cases.Add(Case("recovers_exit_code", "A swept or backfilled crash recovers the exit code from FailureReason or the captain.process_exited event, so the analysis does not say unknown; a text with no code leaves it unset", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission fromReason = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 134.").ConfigureAwait(false);
                Mission fromEvent = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "The agent stopped before finishing.").ConfigureAwait(false);
                ArmadaEvent exited = new ArmadaEvent("captain.process_exited", "Agent process 4242 exited with code 137 for captain bb-captain");
                exited.MissionId = fromEvent.Id;
                await t.Driver.Events.CreateAsync(exited).ConfigureAwait(false);
                Mission unknown = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code unknown.").ConfigureAwait(false);

                BlackBoxBackfillResult sweep = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(3, sweep.Captured, "three crashes captured");
                await bb.Queue.DrainAsync().ConfigureAwait(false);

                BlackBoxRecord reasonRecord = await RecordAsync(t, bb, fromReason.Id).ConfigureAwait(false);
                AssertEqual(134, reasonRecord.Mechanism?.ExitCode, "exit code parsed from FailureReason");
                AssertTrue(reasonRecord.Truncation.Notes.Any(n => n.Contains("Exit code 134 recovered from the mission's failure reason", StringComparison.Ordinal)), "the recovery is noted");
                string reasonWhy = String.Join(" ", reasonRecord.Analysis!.Why.Select(w => w.Statement));
                AssertContains("exited with code 134", reasonWhy, "the analysis names the code");
                AssertFalse(reasonWhy.Contains("code unknown", StringComparison.Ordinal), "the analysis does not say unknown");

                BlackBoxRecord eventRecord = await RecordAsync(t, bb, fromEvent.Id).ConfigureAwait(false);
                AssertEqual(137, eventRecord.Mechanism?.ExitCode, "exit code parsed from captain.process_exited");
                AssertTrue(eventRecord.Truncation.Notes.Any(n => n.Contains("captain.process_exited", StringComparison.Ordinal)), "the event source is noted");

                BlackBoxRecord unknownRecord = await RecordAsync(t, bb, unknown.Id).ConfigureAwait(false);
                AssertNull(unknownRecord.Mechanism?.ExitCode, "no code in the text leaves it unset");

                AssertEqual(1, BlackBoxRecoveredFacts.ParseExitCode("exit code: 1"), "exit code: 1");
                AssertEqual(-1, BlackBoxRecoveredFacts.ParseExitCode("Process exited with code -1"), "negative code");
                AssertNull(BlackBoxRecoveredFacts.ParseExitCode("exited with code unknown"), "unknown");
                AssertNull(BlackBoxRecoveredFacts.ParseExitCode(null), "null");
            }));

            cases.Add(Case("recapture_only_new_failures", "An edit to a captured failed mission (title, priority) is not a new failure and is not re-captured; a status change, a new attempt, or a changed FailureReason or FailureKind is", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission edited = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 1.").ConfigureAwait(false);
                Mission reason = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 1.").ConfigureAwait(false);
                Mission kind = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 1.").ConfigureAwait(false);
                Mission status = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 1.").ConfigureAwait(false);
                Mission again = await BlackBoxTestKit.FailedMissionAsync(t.Driver, null, null, MissionFailureKindEnum.Crash, "Agent process exited with code 1.").ConfigureAwait(false);
                AssertEqual(5, (await bb.Sweeper.SweepAsync().ConfigureAwait(false)).Captured, "first sweep captures all five");
                await Task.Delay(50).ConfigureAwait(false);

                edited = (await t.Driver.Missions.ReadAsync(edited.Id).ConfigureAwait(false))!;
                edited.Title = edited.Title + " (renamed)";
                edited.Priority = edited.Priority + 1;
                edited.LastUpdateUtc = DateTime.UtcNow;
                await t.Driver.Missions.UpdateAsync(edited).ConfigureAwait(false);
                BlackBoxBackfillResult afterEdit = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(0, afterEdit.Captured, "an edit that is not a new failure is not re-captured");
                AssertEqual(1L, await EntryCountAsync(t, edited.Id).ConfigureAwait(false), "still one entry for the edited mission");

                reason = (await t.Driver.Missions.ReadAsync(reason.Id).ConfigureAwait(false))!;
                reason.FailureReason = "Agent process exited with code 137.";
                reason.LastUpdateUtc = DateTime.UtcNow;
                await t.Driver.Missions.UpdateAsync(reason).ConfigureAwait(false);
                kind = (await t.Driver.Missions.ReadAsync(kind.Id).ConfigureAwait(false))!;
                kind.FailureKind = MissionFailureKindEnum.MaxRuntimeExceeded;
                kind.LastUpdateUtc = DateTime.UtcNow;
                await t.Driver.Missions.UpdateAsync(kind).ConfigureAwait(false);
                status = (await t.Driver.Missions.ReadAsync(status.Id).ConfigureAwait(false))!;
                status.Status = MissionStatusEnum.LandingFailed;
                status.LastUpdateUtc = DateTime.UtcNow;
                await t.Driver.Missions.UpdateAsync(status).ConfigureAwait(false);
                again = (await t.Driver.Missions.ReadAsync(again.Id).ConfigureAwait(false))!;
                again.StartedUtc = DateTime.UtcNow;
                again.CompletedUtc = DateTime.UtcNow;
                again.LastUpdateUtc = DateTime.UtcNow;
                await t.Driver.Missions.UpdateAsync(again).ConfigureAwait(false);

                BlackBoxBackfillResult afterFailures = await bb.Sweeper.SweepAsync().ConfigureAwait(false);
                AssertEqual(4, afterFailures.Captured, "a new reason, kind, status or attempt is a new failure");
                AssertEqual(2L, await EntryCountAsync(t, reason.Id).ConfigureAwait(false), "reason change re-captured");
                AssertEqual(2L, await EntryCountAsync(t, kind.Id).ConfigureAwait(false), "kind change re-captured");
                AssertEqual(2L, await EntryCountAsync(t, status.Id).ConfigureAwait(false), "status change re-captured");
                AssertEqual(2L, await EntryCountAsync(t, again.Id).ConfigureAwait(false), "a new attempt that failed again re-captured");
                AssertEqual(0, (await bb.Sweeper.SweepAsync().ConfigureAwait(false)).Captured, "nothing captured twice");
                AssertEqual(1L, await EntryCountAsync(t, edited.Id).ConfigureAwait(false), "the edited mission still has one entry");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box sweeper", cases: cases);
        }

        private static async Task<long> EntryCountAsync(TestDatabase t, string missionId)
        {
            EnumerationResult<BlackBoxEntry> page = await t.Driver.BlackBoxEntries.EnumerateByMissionAsync(missionId, null, new EnumerationQuery { PageSize = 50 }).ConfigureAwait(false);
            return page.TotalRecords;
        }

        private static async Task<BlackBoxRecord> RecordAsync(TestDatabase t, BlackBoxRuntime bb, string missionId)
        {
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync(missionId).ConfigureAwait(false))!;
            return await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
