namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The black box storage and retention queries on every provider: quota eviction candidates, vessels over quota,
    /// cascade candidates (deleted missions, deleted vessels, tenants), the keyset scan (storage target filters and the
    /// migration filter), the narrow storage switch, referenced-key filtering, moving an object's bytes, and the
    /// blackbox_migrations table (migration 104).
    /// </summary>
    public sealed class BlackBoxStorageQueryDatabaseSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Database.BlackBoxStorageQueries";
        private static readonly DateTime _T0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("eviction_candidates", "Candidates are unpinned and not Deleting, oldest first; resolved-only, exclude-current and keep-latest filters; paging", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission live = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                BlackBoxEntry e1 = await Create(t, "vsl_q1", 1, live.Id, current: true);
                BlackBoxEntry e2 = await Create(t, "vsl_q1", 2, "msn_gone", current: true);
                BlackBoxEntry e3 = await Create(t, "vsl_q1", 3, "msn_x", current: false, resolution: BlackBoxResolutionEnum.Resolved);
                BlackBoxEntry e4 = await Create(t, "vsl_q1", 4, "msn_y", current: false, pinned: true);
                BlackBoxEntry e5 = await Create(t, "vsl_q2", 5, "msn_z", current: false);
                BlackBoxEntry e6 = await Create(t, "vsl_q1", 6, "msn_w", current: false);
                await t.Driver.BlackBoxEntries.UpdateStateAsync(e6.Id, BlackBoxStateEnum.Deleting).ConfigureAwait(false);

                EnumerationResult<BlackBoxEntry> all = await t.Driver.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery { KeepLatestPerMission = false }).ConfigureAwait(false);
                AssertEqual(Ids(e1, e2, e3, e5), Ids(all.Objects), "unpinned, not Deleting, oldest first");
                AssertEqual(4L, all.TotalRecords, "count");
                EnumerationResult<BlackBoxEntry> keep = await t.Driver.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery { KeepLatestPerMission = true }).ConfigureAwait(false);
                AssertEqual(Ids(e2, e3, e5), Ids(keep.Objects), "the current entry of a live mission is protected; a purged mission's is not");
                EnumerationResult<BlackBoxEntry> vessel = await t.Driver.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery { VesselId = "vsl_q1", ExcludeCurrent = true }).ConfigureAwait(false);
                AssertEqual(Ids(e3), Ids(vessel.Objects), "per vessel, non-current");
                EnumerationResult<BlackBoxEntry> resolved = await t.Driver.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery { ResolvedOnly = true, KeepLatestPerMission = false }).ConfigureAwait(false);
                AssertEqual(Ids(e3), Ids(resolved.Objects), "resolved only");
                EnumerationResult<BlackBoxEntry> page2 = await t.Driver.BlackBoxEntries.EnumerateEvictionCandidatesAsync(new BlackBoxEvictionQuery { KeepLatestPerMission = false, PageNumber = 2, PageSize = 3 }).ConfigureAwait(false);
                AssertEqual(Ids(e5), Ids(page2.Objects), "second page");
            }));

            cases.Add(Case("vessels_over_quota", "Vessels above the per-vessel limit, most entries first; Deleting entries do not count", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                for (int i = 0; i < 4; i++) await Create(t, "vsl_big", i, "msn_b" + i);
                for (int i = 0; i < 3; i++) await Create(t, "vsl_mid", 10 + i, "msn_m" + i);
                BlackBoxEntry deleting = await Create(t, "vsl_mid", 20, "msn_md");
                await t.Driver.BlackBoxEntries.UpdateStateAsync(deleting.Id, BlackBoxStateEnum.Deleting).ConfigureAwait(false);
                await Create(t, "vsl_small", 30, "msn_s");
                await Create(t, null, 31, "msn_none");
                EnumerationResult<string> over = await t.Driver.BlackBoxEntries.EnumerateVesselsOverQuotaAsync(2, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual("vsl_big,vsl_mid", String.Join(",", over.Objects), "over two");
                AssertEqual(2L, over.TotalRecords, "count");
                AssertEqual("vsl_big", String.Join(",", (await t.Driver.BlackBoxEntries.EnumerateVesselsOverQuotaAsync(3, new EnumerationQuery()).ConfigureAwait(false)).Objects), "over three");
                AssertEqual(0L, (await t.Driver.BlackBoxEntries.EnumerateVesselsOverQuotaAsync(100, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "none");
            }));

            cases.Add(Case("cascade_candidates", "Mission entries whose mission is gone, entries whose vessel is gone (pinned kept), and a tenant's entries (pinned included)", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Mission live = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                Vessel vessel = await t.Driver.Vessels.CreateAsync(new Vessel("BB cascade vessel " + Guid.NewGuid().ToString("N").Substring(0, 6), "https://github.com/test/bbc")).ConfigureAwait(false);
                BlackBoxEntry liveEntry = await Create(t, vessel.Id, 1, live.Id, tenantId: "ten_c1");
                BlackBoxEntry goneMission = await Create(t, vessel.Id, 2, "msn_gone1", tenantId: "ten_c1");
                BlackBoxEntry goneMissionPinned = await Create(t, vessel.Id, 3, "msn_gone2", pinned: true, tenantId: "ten_c1");
                BlackBoxEntry goneVessel = await Create(t, "vsl_gone", 4, live.Id, tenantId: "ten_c2");
                BlackBoxEntry goneVesselPinned = await Create(t, "vsl_gone", 5, live.Id, pinned: true, tenantId: "ten_c2");
                BlackBoxEntry voyage = BlackBoxTestKit.Entry("ten_c2", "msn_gone3", _T0.AddMinutes(6));
                voyage.SubjectType = BlackBoxSubjectTypeEnum.Voyage; voyage.VoyageId = "vyg_x"; voyage.VesselId = vessel.Id;
                voyage = await t.Driver.BlackBoxEntries.CreateAsync(voyage).ConfigureAwait(false);

                EnumerationResult<BlackBoxEntry> missions = await t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.MissionDeleted, null, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(Ids(goneMission), Ids(missions.Objects), "missing mission, unpinned, mission subjects only");
                AssertEqual(Ids(goneMission), Ids((await t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.MissionDeleted, "msn_gone1", new EnumerationQuery()).ConfigureAwait(false)).Objects), "one mission");
                AssertEqual(0, (await t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.MissionDeleted, live.Id, new EnumerationQuery()).ConfigureAwait(false)).Objects.Count, "a live mission does not cascade");
                EnumerationResult<BlackBoxEntry> vessels = await t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.VesselDeleted, null, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(Ids(goneVessel), Ids(vessels.Objects), "missing vessel, unpinned");
                EnumerationResult<BlackBoxEntry> tenant = await t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.TenantDeleted, "ten_c2", new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(Ids(goneVessel, goneVesselPinned, voyage), Ids(tenant.Objects), "every entry of the tenant, pinned included");
                await AssertThrowsAsync<ArgumentNullException>(() => t.Driver.BlackBoxEntries.EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum.TenantDeleted, null, new EnumerationQuery()), "tenant id required").ConfigureAwait(false);
                AssertNotNull(liveEntry, "fixture"); AssertNotNull(goneMissionPinned, "fixture");
            }));

            cases.Add(Case("scan_and_storage_switch", "Keyset scan in id order with target, exclude, vessel and migration filters; the storage switch changes only target and prefix", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BlackBoxEntry db1 = await Create(t, "vsl_s1", 1, "msn_s1");
                BlackBoxEntry db2 = await Create(t, "vsl_s2", 2, "msn_s2");
                BlackBoxEntry s3 = await Create(t, "vsl_s1", 3, "msn_s3", target: BlackBoxStorageTargetEnum.S3);
                BlackBoxEntry s3WithDbObject = await Create(t, "vsl_s1", 4, "msn_s4", target: BlackBoxStorageTargetEnum.S3);
                BlackBoxEntry deleting = await Create(t, "vsl_s1", 5, "msn_s5");
                await t.Driver.BlackBoxEntries.UpdateStateAsync(deleting.Id, BlackBoxStateEnum.Deleting).ConfigureAwait(false);
                await t.Driver.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = s3.Id, Name = "record.json", ContentType = "application/json", StorageKey = "k/s3/record.json" }).ConfigureAwait(false);
                await t.Driver.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = s3WithDbObject.Id, Name = "record.json", ContentType = "application/json", Data = new byte[] { 1 } }).ConfigureAwait(false);
                BlackBoxObject missing = await t.Driver.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = s3.Id, Name = "brief.md", ContentType = "text/markdown", IsMissing = true }).ConfigureAwait(false);

                List<string> ordered = new[] { db1, db2, s3, s3WithDbObject }.Select(e => e.Id).ToList();
                List<string> scanned = new List<string>();
                string? cursor = null;
                for (int i = 0; i < 10; i++)
                {
                    EnumerationResult<BlackBoxEntry> page = await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { AfterId = cursor, PageSize = 3 }).ConfigureAwait(false);
                    if (page.Objects.Count == 0) break;
                    scanned.AddRange(page.Objects.Select(e => e.Id));
                    cursor = page.Objects[page.Objects.Count - 1].Id;
                }
                AssertEqual(String.Join(",", ordered.OrderBy(x => x)), String.Join(",", scanned.OrderBy(x => x)), "every entry once, Deleting skipped");
                AssertEqual(scanned.Count, scanned.Distinct().Count(), "no repeats");
                AssertEqual(Set(db1, db2), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { StorageTarget = BlackBoxStorageTargetEnum.Database }).ConfigureAwait(false)).Objects), "on the database");
                AssertEqual(Set(s3, s3WithDbObject), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { StorageTarget = BlackBoxStorageTargetEnum.Database, ExcludeStorageTarget = true }).ConfigureAwait(false)).Objects), "not on the database");
                AssertEqual(Set(db1, s3, s3WithDbObject), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { VesselId = "vsl_s1" }).ConfigureAwait(false)).Objects), "vessel");
                AssertEqual(Set(db1, db2, s3WithDbObject), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { NeedsMigrationTo = BlackBoxStorageTargetEnum.S3 }).ConfigureAwait(false)).Objects), "needs moving to S3: other targets and a database-resident object (a missing one does not count)");
                AssertEqual(Set(s3, s3WithDbObject), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { NeedsMigrationTo = BlackBoxStorageTargetEnum.Database }).ConfigureAwait(false)).Objects), "needs moving to the database");
                AssertEqual(Set(db2), Set((await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { NeedsMigrationTo = BlackBoxStorageTargetEnum.S3, StorageTarget = BlackBoxStorageTargetEnum.Database, VesselId = "vsl_s2" }).ConfigureAwait(false)).Objects), "combined filters");
                EnumerationResult<BlackBoxEntry> remaining = await t.Driver.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery { AfterId = scanned[0], PageSize = 1 }).ConfigureAwait(false);
                AssertEqual(3L, remaining.TotalRecords, "TotalRecords counts what remains after the cursor");

                DateTime lastUpdate = (await t.Driver.BlackBoxEntries.ReadAsync(db1.Id).ConfigureAwait(false))!.LastUpdateUtc;
                AssertTrue(await t.Driver.BlackBoxEntries.UpdateStorageAsync(db1.Id, BlackBoxStorageTargetEnum.Disk, "armada/blackbox/x/y").ConfigureAwait(false), "switched");
                BlackBoxEntry switched = (await t.Driver.BlackBoxEntries.ReadAsync(db1.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Disk, switched.StorageTarget, "target");
                AssertEqual("armada/blackbox/x/y", switched.StoragePrefix, "prefix");
                AssertEqual(lastUpdate, switched.LastUpdateUtc, "last_update_utc untouched");
                AssertFalse(await t.Driver.BlackBoxEntries.UpdateStorageAsync("bbx_none", BlackBoxStorageTargetEnum.Disk, null).ConfigureAwait(false), "unknown entry");
                AssertNotNull(missing, "fixture");
            }));

            cases.Add(Case("object_keys_and_location", "Referenced storage keys are found in batches; moving bytes sets key and data and clears missing; more than 1000 keys is refused", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BlackBoxEntry entry = await Create(t, "vsl_k", 1, "msn_k", target: BlackBoxStorageTargetEnum.S3);
                List<string> keys = new List<string>();
                for (int i = 0; i < 150; i++)
                {
                    string key = "armada/blackbox/_/_/2026/09/" + entry.Id + "/evidence/check-" + i + ".txt";
                    if (i % 3 == 0) await t.Driver.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = entry.Id, Name = "evidence/check-" + i + ".txt", ContentType = "text/plain", StorageKey = key }).ConfigureAwait(false);
                    keys.Add(key);
                }
                keys.Add(keys[0]);
                List<string> found = await t.Driver.BlackBoxObjects.FilterReferencedStorageKeysAsync(keys).ConfigureAwait(false);
                AssertEqual(50, found.Count, "every third key, duplicates collapsed");
                AssertTrue(found.All(k => keys.IndexOf(k) % 3 == 0), "the referenced ones");
                AssertEqual(0, (await t.Driver.BlackBoxObjects.FilterReferencedStorageKeysAsync(new List<string>()).ConfigureAwait(false)).Count, "empty");
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => t.Driver.BlackBoxObjects.FilterReferencedStorageKeysAsync(Enumerable.Range(0, 1001).Select(i => "k" + i).ToList()), "bounded").ConfigureAwait(false);

                BlackBoxObject obj = await t.Driver.BlackBoxObjects.UpsertAsync(new BlackBoxObject { EntryId = entry.Id, Name = "record.json", ContentType = "application/json", StorageKey = "k/record.json", IsMissing = true }).ConfigureAwait(false);
                AssertTrue(await t.Driver.BlackBoxObjects.SetLocationAsync(obj.Id, null, new byte[] { 7, 8, 9 }).ConfigureAwait(false), "to the database");
                BlackBoxObject inDb = (await t.Driver.BlackBoxObjects.ReadByIdAsync(obj.Id, true).ConfigureAwait(false))!;
                AssertNull(inDb.StorageKey, "no key"); AssertFalse(inDb.IsMissing, "not missing"); AssertEqual("7,8,9", String.Join(",", inDb.Data!), "bytes");
                AssertTrue(await t.Driver.BlackBoxObjects.SetLocationAsync(obj.Id, "k2/record.json", null).ConfigureAwait(false), "to a key");
                BlackBoxObject external = (await t.Driver.BlackBoxObjects.ReadByIdAsync(obj.Id, true).ConfigureAwait(false))!;
                AssertEqual("k2/record.json", external.StorageKey, "key"); AssertNull(external.Data, "bytes cleared");
                AssertFalse(await t.Driver.BlackBoxObjects.SetLocationAsync("bbo_none", null, null).ConfigureAwait(false), "unknown object");
            }));

            cases.Add(Case("migration_jobs", "Migration jobs round-trip, update progress and list active (Pending, Running) oldest first and all newest first", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                BlackBoxStorageMigrationJob a = new BlackBoxStorageMigrationJob { To = BlackBoxStorageTargetEnum.S3, From = BlackBoxStorageTargetEnum.Database, DeleteSource = false, BatchSize = 7, TotalEntries = 12, CreatedUtc = _T0 };
                await t.Driver.BlackBoxMigrations.CreateAsync(a).ConfigureAwait(false);
                BlackBoxStorageMigrationJob b = new BlackBoxStorageMigrationJob { To = BlackBoxStorageTargetEnum.Database, CreatedUtc = _T0.AddMinutes(1), State = BlackBoxJobStateEnum.Running };
                await t.Driver.BlackBoxMigrations.CreateAsync(b).ConfigureAwait(false);
                BlackBoxStorageMigrationJob c = new BlackBoxStorageMigrationJob { To = BlackBoxStorageTargetEnum.Disk, CreatedUtc = _T0.AddMinutes(2), State = BlackBoxJobStateEnum.Completed };
                await t.Driver.BlackBoxMigrations.CreateAsync(c).ConfigureAwait(false);

                BlackBoxStorageMigrationJob read = (await t.Driver.BlackBoxMigrations.ReadAsync(a.Id).ConfigureAwait(false))!;
                AssertStartsWith("bbm_", read.Id, "prefix");
                AssertEqual(BlackBoxJobStateEnum.Pending, read.State); AssertEqual((BlackBoxStorageTargetEnum?)BlackBoxStorageTargetEnum.Database, read.From); AssertEqual(BlackBoxStorageTargetEnum.S3, read.To);
                AssertFalse(read.DeleteSource, "delete source"); AssertEqual(7, read.BatchSize); AssertEqual(12L, read.TotalEntries); AssertEqual(_T0, read.CreatedUtc.ToUniversalTime());
                AssertNull(read.StartedUtc, "not started"); AssertNull((await t.Driver.BlackBoxMigrations.ReadAsync(b.Id).ConfigureAwait(false))!.From, "null from");

                read.State = BlackBoxJobStateEnum.Failed; read.MigratedEntries = 10; read.FailedEntries = 2; read.BytesCopied = 9876543210L; read.LastEntryId = "bbx_last";
                read.LastError = new string('e', 5000); read.StartedUtc = _T0.AddMinutes(5); read.FinishedUtc = _T0.AddMinutes(9);
                AssertTrue(await t.Driver.BlackBoxMigrations.UpdateAsync(read).ConfigureAwait(false), "updated");
                BlackBoxStorageMigrationJob after = (await t.Driver.BlackBoxMigrations.ReadAsync(a.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxJobStateEnum.Failed, after.State); AssertEqual(10L, after.MigratedEntries); AssertEqual(2L, after.FailedEntries); AssertEqual(9876543210L, after.BytesCopied);
                AssertEqual("bbx_last", after.LastEntryId); AssertEqual(2000, after.LastError!.Length, "error clipped"); AssertEqual(_T0.AddMinutes(9), after.FinishedUtc!.Value.ToUniversalTime());

                EnumerationResult<BlackBoxStorageMigrationJob> active = await t.Driver.BlackBoxMigrations.EnumerateActiveAsync(new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(b.Id, String.Join(",", active.Objects.Select(j => j.Id)), "only Running/Pending");
                EnumerationResult<BlackBoxStorageMigrationJob> all = await t.Driver.BlackBoxMigrations.EnumerateAsync(new EnumerationQuery { PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(c.Id + "," + b.Id, String.Join(",", all.Objects.Select(j => j.Id)), "newest first");
                AssertEqual(3L, all.TotalRecords, "count");
                AssertNull(await t.Driver.BlackBoxMigrations.ReadAsync("bbm_none").ConfigureAwait(false), "unknown");
                AssertFalse(await t.Driver.BlackBoxMigrations.UpdateAsync(new BlackBoxStorageMigrationJob()).ConfigureAwait(false), "update of an unknown job");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box storage and retention queries", cases: cases);
        }

        private static async Task<BlackBoxEntry> Create(TestDatabase t, string? vesselId, int minutes, string? missionId, bool current = true, bool pinned = false,
            BlackBoxResolutionEnum resolution = BlackBoxResolutionEnum.Open, BlackBoxStorageTargetEnum target = BlackBoxStorageTargetEnum.Database, string? tenantId = "ten_q")
        {
            BlackBoxEntry e = BlackBoxTestKit.Entry(tenantId, missionId, _T0.AddMinutes(minutes));
            e.VesselId = vesselId;
            e.IsCurrent = current;
            e.Pinned = pinned;
            e.Resolution = resolution;
            e.StorageTarget = target;
            e.TotalBytes = 1000 + minutes;
            return await t.Driver.BlackBoxEntries.CreateAsync(e).ConfigureAwait(false);
        }

        private static string Ids(params BlackBoxEntry[] entries)
        {
            return String.Join(",", entries.Select(e => e.Id));
        }

        private static string Ids(IEnumerable<BlackBoxEntry> entries)
        {
            return String.Join(",", entries.Select(e => e.Id));
        }

        private static string Set(params BlackBoxEntry[] entries)
        {
            return String.Join(",", entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
        }

        private static string Set(IEnumerable<BlackBoxEntry> entries)
        {
            return String.Join(",", entries.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag, TestTags.Database });
        }
    }
}
