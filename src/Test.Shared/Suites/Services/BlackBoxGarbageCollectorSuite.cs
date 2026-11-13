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
    /// BlackBoxGarbageCollector expiry: expired entries go with every row, pinned and current entries stay, dry runs delete nothing, and a failing store leaves the entry Deleting (hidden) until a later run finishes it.
    /// </summary>
    public sealed class BlackBoxGarbageCollectorSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxGarbageCollector";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("expiry_pinned_current_dry_run", "Expired entries are deleted; pinned and the current entry of a live mission stay; a dry run reports without deleting", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, null, sink);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string old = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                string current = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                Mission other = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string pinned = (await bb.Capture.RecordMissionFailureAsync(other, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                foreach (string id in new[] { old, current, pinned }) await t.Driver.BlackBoxEntries.UpdateExpiresAsync(id, DateTime.UtcNow.AddDays(-1)).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(pinned, true, DateTime.UtcNow.AddDays(-1)).ConfigureAwait(false);

                BlackBoxGcReport dry = await bb.GarbageCollector.RunAsync(true).ConfigureAwait(false);
                AssertTrue(dry.DryRun, "dry run"); AssertEqual(1, dry.Expired, "would expire only the superseded attempt");
                AssertEqual(old, dry.CandidateEntryIds.Single(), "candidate");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(old).ConfigureAwait(false), "dry run deleted nothing");
                AssertEqual(0, sink.Count(BlackBoxEventTypes.GcCompleted), "dry run emits no completion event");

                BlackBoxGcReport real = await bb.GarbageCollector.RunAsync(false).ConfigureAwait(false);
                AssertEqual(1, real.Expired); AssertTrue(real.BytesFreed > 0, "bytes freed");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(old, null, true).ConfigureAwait(false), "expired entry gone");
                AssertEqual(0L, (await t.Driver.BlackBoxObjects.EnumerateAsync(old, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "its objects gone");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(current).ConfigureAwait(false), "current entry of a live mission kept");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(pinned).ConfigureAwait(false), "pinned kept");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.GcCompleted), "completion event");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.Deleted), "deleted event");
                AssertNotNull(bb.GarbageCollector.LastReport, "last report");

                await t.Driver.Missions.DeleteAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(1, (await bb.GarbageCollector.RunAsync(false).ConfigureAwait(false)).Expired, "the purged mission's current entry expires");
            }));

            cases.Add(Case("two_phase_delete_on_store_failure", "When the store delete fails the entry stays Deleting and hidden; the next run finishes it", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.S3;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                ScriptedBlackBoxStore store = new ScriptedBlackBoxStore(BlackBoxStorageTargetEnum.S3);
                bb.Stores.Register(BlackBoxStorageTargetEnum.S3, _ => store);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string old = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
                AssertTrue(store.Objects.Count >= 4, "objects in the external store: " + store.Objects.Count);
                await t.Driver.BlackBoxEntries.UpdateExpiresAsync(old, DateTime.UtcNow.AddDays(-1)).ConfigureAwait(false);
                store.FailDeletes = true;
                BlackBoxGcReport failed = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(1, failed.Failures, "failure counted"); AssertEqual(0, failed.Expired, "not deleted");
                BlackBoxEntry deleting = (await t.Driver.BlackBoxEntries.ReadAsync(old, null, true).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStateEnum.Deleting, deleting.State, "left Deleting");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(old).ConfigureAwait(false), "hidden from readers");
                await AssertThrowsAsync<BlackBoxDeletingException>(() => bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), old), "service says Deleting").ConfigureAwait(false);
                store.FailDeletes = false;
                BlackBoxGcReport retried = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(1, retried.RetriedDeletes, "finished on the next run");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(old, null, true).ConfigureAwait(false), "gone");
            }));

            cases.Add(Case("retention_policy", "Expiry: pinned never, vessel override, 0 keeps forever, resolution shortens", TestTags.Positive, () =>
            {
                BlackBoxRetentionSettings policy = new BlackBoxRetentionSettings { RetentionDays = 180, ResolvedRetentionDays = 30 };
                DateTime created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                BlackBoxEntry e = new BlackBoxEntry { CreatedUtc = created };
                AssertEqual(created.AddDays(180), BlackBoxRetentionPolicy.ComputeExpiry(e, policy, null), "default");
                AssertEqual(created.AddDays(7), BlackBoxRetentionPolicy.ComputeExpiry(e, policy, 7), "vessel override");
                AssertNull(BlackBoxRetentionPolicy.ComputeExpiry(e, policy, 0), "vessel 0 keeps forever");
                e.Resolution = BlackBoxResolutionEnum.Resolved; e.ResolvedUtc = created.AddDays(1);
                AssertEqual(created.AddDays(31), BlackBoxRetentionPolicy.ComputeExpiry(e, policy, null), "resolved sooner");
                e.Pinned = true;
                AssertNull(BlackBoxRetentionPolicy.ComputeExpiry(e, policy, null), "pinned");
                policy.RetentionDays = 99999; policy.GcBatchSize = 1; policy.MaxTotalBytes = 1;
                AssertEqual(3650, policy.RetentionDays, "clamped"); AssertEqual(10, policy.GcBatchSize, "clamped"); AssertEqual(16777216L, policy.MaxTotalBytes, "clamped");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box garbage collection", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
