namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box retention and garbage collection (plan section 16.5): age and resolved expiry, retentionDays 0, the
    /// per-vessel and global quotas and their eviction order, vessel, mission and tenant cascades (garbage collection and
    /// the lifecycle hooks), dry runs, expiry recompute on a policy or vessel change, blackbox.gc_completed counts, and the
    /// Disk orphan sweep grace period.
    /// </summary>
    public sealed class BlackBoxRetentionSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxRetention";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("resolved_retention", "A resolved entry expires resolvedRetentionDays after resolution, sooner than retentionDays, and GC deletes it", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, null, sink);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string old = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
                BlackBoxEntry resolved = await bb.Service.ResolveAsync(BlackBoxTestKit.Admin(), old, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Resolved, Note = "fixed" }).ConfigureAwait(false);
                AssertTrue(Math.Abs((resolved.ExpiresUtc!.Value.ToUniversalTime() - resolved.ResolvedUtc!.Value.ToUniversalTime().AddDays(30)).TotalMinutes) < 1, "expires 30 days after resolution");
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Expired, "not yet");
                bb.GarbageCollector.Clock = () => DateTime.UtcNow.AddDays(31);
                BlackBoxGcReport report = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(1, report.Expired, "resolved entry expired");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(old, null, true).ConfigureAwait(false), "deleted");
                BlackBoxGcReport payload = (BlackBoxGcReport)sink.Events.Last(e => e.EventType == BlackBoxEventTypes.GcCompleted).Payload!;
                AssertEqual(1, payload.Expired, "blackbox.gc_completed counts it");
                AssertTrue(payload.BytesFreed > 0, "bytes freed reported");
            }));

            cases.Add(Case("retention_zero_and_pinned", "retentionDays 0 expires nothing by age; pinned entries never expire; unpinning recomputes", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Retention.RetentionDays = 0;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string a = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
                AssertNull((await t.Driver.BlackBoxEntries.ReadAsync(a).ConfigureAwait(false))!.ExpiresUtc, "no expiry");
                bb.GarbageCollector.Clock = () => DateTime.UtcNow.AddYears(20);
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Expired, "nothing expires by age");

                settings.BlackBox.Retention.RetentionDays = 10;
                BlackBoxEntry pinned = await bb.Service.PinAsync(BlackBoxTestKit.Admin(), a, true).ConfigureAwait(false);
                AssertNull(pinned.ExpiresUtc, "pinned never expires");
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Expired, "pinned kept");
                BlackBoxEntry unpinned = await bb.Service.PinAsync(BlackBoxTestKit.Admin(), a, false).ConfigureAwait(false);
                AssertNotNull(unpinned.ExpiresUtc, "unpin recomputes the expiry");
                AssertEqual(1, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Expired, "then it expires");
            }));

            cases.Add(Case("vessel_quota", "Above maxEntriesPerVessel the oldest unpinned, non-current entries are evicted; pinned and current entries stay; a dry run reports the same", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Retention.MaxEntriesPerVessel = 10;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Vessel vessel = await NewVesselAsync(t).ConfigureAwait(false);
                DateTime t0 = DateTime.UtcNow.AddDays(-5);
                List<BlackBoxEntry> entries = new List<BlackBoxEntry>();
                for (int i = 0; i < 14; i++) entries.Add(await RawAsync(t, vessel.Id, t0.AddMinutes(i), current: false).ConfigureAwait(false));
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(entries[0].Id, true, null).ConfigureAwait(false);
                BlackBoxEntry current = entries[1];
                current.IsCurrent = true;
                await t.Driver.BlackBoxEntries.UpdateAsync(current).ConfigureAwait(false);
                Vessel other = await NewVesselAsync(t).ConfigureAwait(false);
                await RawAsync(t, other.Id, t0, current: false).ConfigureAwait(false);

                BlackBoxGcReport dry = await bb.GarbageCollector.RunAsync(true).ConfigureAwait(false);
                AssertEqual(4, dry.Evicted, "dry run would evict four");
                AssertEqual(String.Join(",", entries.Skip(2).Take(4).Select(e => e.Id)), String.Join(",", dry.CandidateEntryIds), "the oldest unpinned non-current");
                AssertEqual(15L, await t.Driver.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest()).ConfigureAwait(false), "dry run deleted nothing");

                BlackBoxGcReport real = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(4, real.Evicted, "evicted four");
                AssertEqual(10L, await t.Driver.BlackBoxEntries.CountAsync(new BlackBoxEnumerateRequest { VesselId = vessel.Id }).ConfigureAwait(false), "at the limit");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(entries[0].Id).ConfigureAwait(false), "pinned stays");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(current.Id).ConfigureAwait(false), "current stays");
                foreach (BlackBoxEntry gone in entries.Skip(2).Take(4)) AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(gone.Id, null, true).ConfigureAwait(false), "evicted " + gone.Id);
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Evicted, "nothing more");
            }));

            cases.Add(Case("total_bytes_quota", "Above maxTotalBytes the oldest unpinned resolved entries go first, then the oldest unpinned", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Retention.MaxTotalBytes = 16L * 1024 * 1024;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                DateTime t0 = DateTime.UtcNow.AddDays(-3);
                long ten = 10L * 1024 * 1024;
                BlackBoxEntry oldestOpen = await RawAsync(t, null, t0, current: false, bytes: ten).ConfigureAwait(false);
                BlackBoxEntry olderOpen = await RawAsync(t, null, t0.AddMinutes(1), current: false, bytes: ten).ConfigureAwait(false);
                BlackBoxEntry newestResolved = await RawAsync(t, null, t0.AddMinutes(2), current: false, bytes: ten, resolution: BlackBoxResolutionEnum.Resolved).ConfigureAwait(false);
                BlackBoxEntry pinned = await RawAsync(t, null, t0.AddMinutes(-10), current: false, bytes: 1024).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(pinned.Id, true, null).ConfigureAwait(false);

                BlackBoxGcReport dry = await bb.GarbageCollector.RunAsync(true).ConfigureAwait(false);
                AssertEqual(newestResolved.Id + "," + oldestOpen.Id, String.Join(",", dry.CandidateEntryIds), "resolved first, then the oldest open");
                BlackBoxGcReport real = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(2, real.Evicted, "two evicted (30 MB over a 16 MB cap)");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(newestResolved.Id, null, true).ConfigureAwait(false), "resolved went first");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(oldestOpen.Id, null, true).ConfigureAwait(false), "then the oldest");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(olderOpen.Id).ConfigureAwait(false), "under the cap now");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(pinned.Id).ConfigureAwait(false), "pinned never evicted");
            }));

            cases.Add(Case("vessel_cascade", "Deleting a vessel deletes its entries (deleteWithVessel), at once through the hook and otherwise at the next run; off keeps them", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbcascade");
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Vessel hooked = await NewVesselAsync(t).ConfigureAwait(false);
                Vessel swept = await NewVesselAsync(t).ConfigureAwait(false);
                Vessel kept = await NewVesselAsync(t).ConfigureAwait(false);
                string a = await CaptureOnVesselAsync(bb, t, hooked.Id).ConfigureAwait(false);
                string b = await CaptureOnVesselAsync(bb, t, swept.Id).ConfigureAwait(false);
                string pinnedOnSwept = await CaptureOnVesselAsync(bb, t, swept.Id).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(pinnedOnSwept, true, null).ConfigureAwait(false);
                List<BlackBoxObjectRef> refs = await RefsAsync(t, a).ConfigureAwait(false);

                await DeleteVesselAsync(t, hooked.Id, a).ConfigureAwait(false);
                await bb.Hooks.OnVesselDeletedAsync(hooked.Id).ConfigureAwait(false);
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(a, null, true).ConfigureAwait(false), "hook deleted the entry");
                foreach (BlackBoxObjectRef reference in refs) AssertFalse(await bb.Stores.Get(BlackBoxStorageTargetEnum.Disk).ExistsAsync(reference).ConfigureAwait(false), "and its objects");

                await DeleteVesselAsync(t, swept.Id, b, pinnedOnSwept).ConfigureAwait(false);
                BlackBoxGcReport dry = await bb.GarbageCollector.RunAsync(true).ConfigureAwait(false);
                AssertEqual(1, dry.Cascaded, "dry run counts the cascade");
                BlackBoxGcReport real = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(1, real.Cascaded, "the next run cascades");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(b, null, true).ConfigureAwait(false), "deleted");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(pinnedOnSwept).ConfigureAwait(false), "a pinned entry survives its vessel");

                settings.BlackBox.Retention.DeleteWithVessel = false;
                string c = await CaptureOnVesselAsync(bb, t, kept.Id).ConfigureAwait(false);
                await DeleteVesselAsync(t, kept.Id, c).ConfigureAwait(false);
                await bb.Hooks.OnVesselDeletedAsync(kept.Id).ConfigureAwait(false);
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Cascaded, "off: no cascade");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(c).ConfigureAwait(false), "kept");
            }));

            cases.Add(Case("mission_cascade", "deleteWithMission false keeps a purged mission's entries (readable by a tenant admin with the denormalized title); true deletes them", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                string tenant = "ten_bbret_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await t.Driver.Tenants.CreateAsync(new TenantMetadata { Id = tenant, Name = tenant }).ConfigureAwait(false);
                Mission keptMission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, tenant, null).ConfigureAwait(false);
                string kept = (await bb.Capture.RecordMissionFailureAsync(keptMission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await t.Driver.Missions.DeleteAsync(keptMission.Id).ConfigureAwait(false);
                await bb.Hooks.OnMissionDeletedAsync(keptMission.Id).ConfigureAwait(false);
                AssertEqual(0, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Cascaded, "no cascade by default");
                BlackBoxEntry read = await bb.Service.GetEntryAsync(BlackBoxTestKit.User(tenant, "usr_other", true), kept).ConfigureAwait(false);
                AssertEqual(keptMission.Title, read.MissionTitle, "denormalized title");
                AssertNotNull(read.Record, "record readable after the purge");

                settings.BlackBox.Retention.DeleteWithMission = true;
                Mission goneMission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, tenant, null).ConfigureAwait(false);
                string gone = (await bb.Capture.RecordMissionFailureAsync(goneMission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await t.Driver.Missions.DeleteAsync(goneMission.Id).ConfigureAwait(false);
                await bb.Hooks.OnMissionDeletedAsync(goneMission.Id).ConfigureAwait(false);
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(gone, null, true).ConfigureAwait(false), "hook deleted the purged mission's entry");
                AssertEqual(1, (await bb.GarbageCollector.RunAsync().ConfigureAwait(false)).Cascaded, "the next run also cascades the earlier purge");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(kept, null, true).ConfigureAwait(false), "now deleted");
            }));

            cases.Add(Case("tenant_deletion", "Deleting a tenant deletes every entry and object of the tenant, pinned ones included, and nothing of other tenants", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                BlackBoxEntry a = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_gone", "msn_t1")).ConfigureAwait(false);
                BlackBoxEntry pinned = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_gone", "msn_t2")).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(pinned.Id, true, null).ConfigureAwait(false);
                await bb.Objects.WriteAsync(a, BlackBoxObjectNames.Record, "application/json", System.Text.Encoding.UTF8.GetBytes("{}"), true).ConfigureAwait(false);
                BlackBoxEntry other = await t.Driver.BlackBoxEntries.CreateAsync(BlackBoxTestKit.Entry("ten_stays", "msn_t3")).ConfigureAwait(false);
                await bb.Hooks.OnTenantDeletedAsync("ten_gone").ConfigureAwait(false);
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(a.Id, null, true).ConfigureAwait(false), "entry gone");
                AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(pinned.Id, null, true).ConfigureAwait(false), "pinned gone too");
                AssertEqual(0L, (await t.Driver.BlackBoxObjects.EnumerateAsync(a.Id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "objects gone");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(other.Id).ConfigureAwait(false), "other tenant untouched");
            }));

            cases.Add(Case("policy_change_recompute", "Changing retentionDays recomputes expires_utc for every entry; a vessel override change recomputes that vessel's entries", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Vessel vessel = await NewVesselAsync(t).ConfigureAwait(false);
                DateTime created = DateTime.UtcNow.AddDays(-1);
                BlackBoxEntry onVessel = await RawAsync(t, vessel.Id, created, current: false).ConfigureAwait(false);
                BlackBoxEntry plain = await RawAsync(t, null, created, current: false).ConfigureAwait(false);
                BlackBoxEntry pinned = await RawAsync(t, null, created, current: false).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdatePinnedAsync(pinned.Id, true, null).ConfigureAwait(false);

                BlackBoxRetentionSettings previous = settings.BlackBox.Retention.Clone();
                settings.BlackBox.Retention.RetentionDays = 30;
                await bb.ApplySettingsChanged(previous).ConfigureAwait(false);
                AssertEqual(created.AddDays(30).ToString("u"), Expiry(await t.Driver.BlackBoxEntries.ReadAsync(plain.Id).ConfigureAwait(false)), "recomputed");
                AssertEqual(created.AddDays(30).ToString("u"), Expiry(await t.Driver.BlackBoxEntries.ReadAsync(onVessel.Id).ConfigureAwait(false)), "vessel without override");
                AssertNull((await t.Driver.BlackBoxEntries.ReadAsync(pinned.Id).ConfigureAwait(false))!.ExpiresUtc, "pinned stays without expiry");

                vessel.BlackBoxRetentionDays = 3;
                await t.Driver.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                await bb.Hooks.OnVesselUpdatedAsync(vessel.Id).ConfigureAwait(false);
                await bb.GarbageCollector.StartRecompute(vessel.Id).ConfigureAwait(false);
                AssertEqual(created.AddDays(3).ToString("u"), Expiry(await t.Driver.BlackBoxEntries.ReadAsync(onVessel.Id).ConfigureAwait(false)), "vessel override applied");
                AssertEqual(created.AddDays(30).ToString("u"), Expiry(await t.Driver.BlackBoxEntries.ReadAsync(plain.Id).ConfigureAwait(false)), "other entries untouched");
                AssertEqual(0, await bb.GarbageCollector.RecomputeExpiryAsync().ConfigureAwait(false), "idempotent");
            }));

            cases.Add(Case("disk_orphans", "On Disk an orphan older than orphanGraceHours is deleted and counted; a younger one is kept", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bborphan");
                settings.BlackBox.Retention.OrphanGraceHours = 2;
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
                IBlackBoxStore store = bb.Stores.Get(BlackBoxStorageTargetEnum.Disk);
                BlackBoxObjectRef oldOrphan = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "o", StorageKey = settings.BlackBox.Storage.KeyPrefix + "/_/_/2020/01/bbx_old/record.json" };
                BlackBoxObjectRef youngOrphan = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "y", StorageKey = settings.BlackBox.Storage.KeyPrefix + "/_/_/2026/10/bbx_young/record.json" };
                BlackBoxObjectRef outside = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "x", StorageKey = "not-the-prefix/file.txt" };
                foreach (BlackBoxObjectRef r in new[] { oldOrphan, youngOrphan, outside }) await store.WriteAsync(new BlackBoxObjectWrite { Reference = r, Data = new byte[] { 1, 2, 3 } }).ConfigureAwait(false);
                string path = ((BlobjectBlackBoxStore)store).ResolveDiskPath(oldOrphan.StorageKey!);
                System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-3));
                BlackBoxGcReport report = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                AssertEqual(1, report.OrphansRemoved, "one orphan past the grace period");
                AssertFalse(await store.ExistsAsync(oldOrphan).ConfigureAwait(false), "old orphan deleted");
                AssertTrue(await store.ExistsAsync(youngOrphan).ConfigureAwait(false), "young orphan kept");
                AssertTrue(await store.ExistsAsync(outside).ConfigureAwait(false), "keys outside the key prefix are never touched");
                AssertEqual(1, ((BlackBoxGcReport)sink.Events.Last(e => e.EventType == BlackBoxEventTypes.GcCompleted).Payload!).OrphansRemoved, "reported in blackbox.gc_completed");
            }));

            cases.Add(Case("other_retention_skips_black_box", "RetentionService and DataExpiryService never delete black box rows; the black box has its own policy", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await new Armada.Core.Services.RetentionService(t.Driver, settings, BlackBoxTestKit.Logging(), () => DateTime.UtcNow.AddYears(5)).PruneAsync().ConfigureAwait(false);
                if (TestDatabaseConfig.IsSqlite) await new Armada.Core.Services.DataExpiryService(BlackBoxTestKit.Logging(), t.ConnectionString, 1).PurgeExpiredDataAsync().ConfigureAwait(false);
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false), "entry kept");
                AssertTrue((await t.Driver.BlackBoxObjects.EnumerateAsync(id, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords > 0, "objects kept");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box retention and garbage collection", cases: cases);
        }

        private static async Task<Vessel> NewVesselAsync(TestDatabase t)
        {
            return await t.Driver.Vessels.CreateAsync(new Vessel("BB retention " + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/bbr")).ConfigureAwait(false);
        }

        private static async Task<BlackBoxEntry> RawAsync(TestDatabase t, string? vesselId, DateTime created, bool current, long bytes = 1000, BlackBoxResolutionEnum resolution = BlackBoxResolutionEnum.Open)
        {
            BlackBoxEntry e = BlackBoxTestKit.Entry("ten_ret", "msn_" + Guid.NewGuid().ToString("N").Substring(0, 10), created);
            e.VesselId = vesselId;
            e.IsCurrent = current;
            e.TotalBytes = bytes;
            e.Resolution = resolution;
            if (resolution != BlackBoxResolutionEnum.Open) e.ResolvedUtc = created;
            e.ExpiresUtc = created.AddDays(180);
            return await t.Driver.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
        }

        private static async Task<string> CaptureOnVesselAsync(BlackBoxRuntime bb, TestDatabase t, string vesselId)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
            mission.VesselId = vesselId;
            await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
            AssertNotNull(id, "captured");
            AssertEqual(vesselId, (await t.Driver.BlackBoxEntries.ReadAsync(id!).ConfigureAwait(false))!.VesselId, "entry on the vessel");
            return id!;
        }

        private static async Task DeleteVesselAsync(TestDatabase t, string vesselId, params string[] entryIds)
        {
            // Like the vessel delete paths: the vessel's missions go first, then the vessel.
            foreach (string entryId in entryIds)
            {
                BlackBoxEntry? entry = await t.Driver.BlackBoxEntries.ReadAsync(entryId).ConfigureAwait(false);
                if (entry?.MissionId != null) await t.Driver.Missions.DeleteAsync(entry.MissionId).ConfigureAwait(false);
            }
            await t.Driver.Vessels.DeleteAsync(vesselId).ConfigureAwait(false);
        }

        private static async Task<List<BlackBoxObjectRef>> RefsAsync(TestDatabase t, string entryId)
        {
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(entryId).ConfigureAwait(false))!;
            return (await t.Driver.BlackBoxObjects.EnumerateAsync(entryId, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects.Select(o => BlackBoxObjectRepository.RefOf(entry, o)).ToList();
        }

        private static string? Expiry(BlackBoxEntry? entry)
        {
            return entry?.ExpiresUtc?.ToUniversalTime().ToString("u");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
