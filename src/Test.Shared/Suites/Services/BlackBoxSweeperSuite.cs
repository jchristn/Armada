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
                AssertEqual(BlackBoxStepEnum.Unknown, (await t.Driver.BlackBoxEntries.ReadCurrentByMissionAsync(b.Id).ConfigureAwait(false))!.Step, "unknown when ambiguous");
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

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box sweeper", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
