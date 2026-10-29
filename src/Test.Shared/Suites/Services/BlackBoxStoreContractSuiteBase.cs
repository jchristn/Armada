namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.BlackBoxStorage;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The shared black box storage contract (plan section 16.4), run against one storage target per derived suite:
    /// capture round trip with the key layout, whole and ranged reads with SHA-256 verification, tampered objects read as
    /// corrupt, objects deleted out of band read as missing and are marked ObjectMissing by garbage collection, deletes
    /// remove the store's bytes, and, for external targets, raw store operations, the orphan sweep, a target change that
    /// keeps old entries readable, and a storage migration from the database that resumes after an interruption.
    /// Targets whose emulator is unavailable are reported as skipped with the reason.
    /// </summary>
    public abstract class BlackBoxStoreContractSuiteBase : IArmadaTestSuite
    {
        /// <summary>
        /// Suite id.
        /// </summary>
        protected abstract string SuiteId { get; }

        /// <summary>
        /// Suite display name.
        /// </summary>
        protected abstract string DisplayName { get; }

        /// <summary>
        /// Target under test.
        /// </summary>
        protected abstract BlackBoxStorageTargetEnum Target { get; }

        /// <summary>
        /// Target that the Database suite migrates to (the Database suite cannot migrate to itself).
        /// </summary>
        protected virtual BlackBoxStorageTargetEnum MigrationTarget => Target;

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            string? skip = Target == BlackBoxStorageTargetEnum.Database ? null : BlackBoxStorageEmulators.SkipReason(Target);
            bool external = Target != BlackBoxStorageTargetEnum.Database;
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("capture_roundtrip", "A captured entry lands on the target under the key layout; whole and ranged reads verify", TestTags.Positive, skip, CaptureRoundTripAsync));
            cases.Add(Case("tampered_reads_corrupt", "Bytes changed in the store read as BlackBoxObjectCorrupt", TestTags.Negative, skip, TamperedAsync));
            cases.Add(Case("deleted_out_of_band", "Bytes deleted in the store read as missing; garbage collection marks the object and entry ObjectMissing", TestTags.Negative, skip, DeletedOutOfBandAsync));
            cases.Add(Case("gc_deletes_store_bytes", "Deleting an expired entry removes its bytes from the store and every row", TestTags.Positive, skip, GcDeletesAsync));
            cases.Add(Case("migration_resumes", "A migration from the database to " + MigrationTarget + " copies and verifies every object, switches the entries, deletes the source and resumes after an interruption; migrating back restores the database copy", TestTags.Positive, MigrationSkip(skip), MigrationAsync));
            if (external)
            {
                bool enumerationUnreliable = Target == BlackBoxStorageTargetEnum.GoogleCloud;
                string? enumSkip = skip ?? (enumerationUnreliable ? "fake-gcs-server writes object timestamps (trailing-zero microseconds) that Google.Apis cannot always parse, so listing through Blobject.GoogleCloud 6.1.2 fails intermittently against the emulator; real GCS uses millisecond timestamps" : null);
                cases.Add(Case("store_operations", "Write, exists, ranged reads, delete, delete-many (missing keys succeed), health", TestTags.Positive, skip, StoreOperationsAsync));
                cases.Add(Case("store_enumerate", "Enumerate lists the keys under a prefix with sizes and times", TestTags.Positive, enumSkip, StoreEnumerateAsync));
                cases.Add(Case("orphan_sweep", "An unreferenced key older than orphanGraceHours is deleted; a younger one and referenced keys are kept", TestTags.Positive, enumSkip, OrphanSweepAsync));
                cases.Add(Case("target_change_keeps_old_readable", "Changing the target from Database moves new entries only; old entries stay readable where they were written", TestTags.Positive, skip, TargetChangeAsync));
            }
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: DisplayName, cases: cases);
        }

        private string? MigrationSkip(string? skip)
        {
            if (skip != null) return skip;
            return MigrationTarget == BlackBoxStorageTargetEnum.Database ? "the Database target cannot migrate to itself" : null;
        }

        private TestCaseDescriptor Case(string caseId, string displayName, string tag, string? skipReason, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName,
                executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag }, skip: skipReason != null, skipReason: skipReason);
        }

        #region Fixture

        private async Task<ArmadaSettings> SettingsAsync(BlackBoxStorageTargetEnum target)
        {
            ArmadaSettings settings = BlackBoxTestKit.Settings();
            if (target != BlackBoxStorageTargetEnum.Database)
            {
                BlackBoxTargetEnvironment env = await BlackBoxStorageEmulators.GetAsync(target).ConfigureAwait(false);
                env.Apply(settings.BlackBox);
            }
            return settings;
        }

        private static async Task<string> CaptureAsync(BlackBoxRuntime bb, TestDatabase t, string? tenantId = null)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver, tenantId, tenantId == null ? null : "usr_contract").ConfigureAwait(false);
            mission.AgentOutput = String.Join("\n", Enumerable.Range(0, 400).Select(i => "agent output line " + i));
            await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
            AssertNotNull(id, "captured");
            return id!;
        }

        private static async Task<List<BlackBoxObject>> ObjectsAsync(TestDatabase t, string entryId)
        {
            return (await t.Driver.BlackBoxObjects.EnumerateAsync(entryId, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects;
        }

        private static async Task<byte[]> StoredAsync(BlackBoxRuntime bb, BlackBoxEntry entry, BlackBoxObject obj)
        {
            BlackBoxObjectRef reference = BlackBoxObjectRepository.RefOf(entry, obj);
            using (Stream stream = await bb.Stores.Get(reference.Target).OpenReadAsync(reference, 0, null).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        #endregion

        #region Cases

        private async Task CaptureRoundTripAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
            string id = await CaptureAsync(bb, t).ConfigureAwait(false);
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
            AssertEqual(Target, entry.StorageTarget, "entry target");
            AssertEqual(0, sink.Count(BlackBoxEventTypes.StorageFallback), "no fallback");
            List<BlackBoxObject> objects = await ObjectsAsync(t, id).ConfigureAwait(false);
            AssertTrue(objects.Count >= 5, "record, three briefs and evidence: " + objects.Count);
            AssertTrue(objects.All(o => !o.IsMissing), "nothing missing");
            if (Target == BlackBoxStorageTargetEnum.Database)
            {
                AssertTrue(objects.All(o => o.StorageKey == null), "database objects have no key");
            }
            else
            {
                string expectedPrefix = settings.BlackBox.Storage.KeyPrefix + "/_/_/" + entry.CreatedUtc.ToString("yyyy") + "/" + entry.CreatedUtc.ToString("MM") + "/" + id + "/";
                AssertEqual(expectedPrefix.TrimEnd('/'), entry.StoragePrefix, "entry prefix");
                foreach (BlackBoxObject obj in objects) AssertEqual(expectedPrefix + obj.Name, obj.StorageKey, "key of " + obj.Name);
                AssertNull((await t.Driver.BlackBoxObjects.ReadByIdAsync(objects[0].Id, true).ConfigureAwait(false))!.Data, "no bytes in the database");
                if (Target == BlackBoxStorageTargetEnum.Disk)
                {
                    BlobjectBlackBoxStore disk = (BlobjectBlackBoxStore)bb.Stores.Get(BlackBoxStorageTargetEnum.Disk);
                    foreach (BlackBoxObject obj in objects) AssertTrue(File.Exists(Path.Combine(disk.DiskRoot!, obj.StorageKey!.Replace('/', Path.DirectorySeparatorChar))), "file for " + obj.Name);
                }
            }

            BlackBoxEntry withRecord = await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
            AssertNotNull(withRecord.Record, "record loads");
            AssertEqual(id, withRecord.Record!.EntryId, "record entry id");
            BlackBoxObjectReadResult whole = await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false);
            string text = Encoding.UTF8.GetString(whole.Data);
            AssertTrue(whole.Verified, "SHA-256 verified");
            AssertContains("agent output line 399", text, "whole read");
            BlackBoxObjectReadResult range = await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput, 6, 6).ConfigureAwait(false);
            AssertEqual("output", Encoding.UTF8.GetString(range.Data), "ranged read");
            AssertEqual(whole.TotalBytes, range.TotalBytes, "total bytes");

            // An identity-encoded object reads ranges straight from the store.
            BlackBoxObject plain = await bb.Objects.WriteAsync(entry, "evidence/plain.txt", "text/plain", Encoding.UTF8.GetBytes("0123456789abcdef"), false).ConfigureAwait(false);
            AssertEqual("identity", plain.ContentEncoding, "identity");
            AssertEqual("6789a", Encoding.UTF8.GetString((await bb.Objects.ReadAsync(entry, "evidence/plain.txt", 6, 5).ConfigureAwait(false)).Data), "identity range");
            AssertEqual("", Encoding.UTF8.GetString((await bb.Objects.ReadAsync(entry, "evidence/plain.txt", 100, 5).ConfigureAwait(false)).Data), "range past the end");
            AssertTrue((await bb.Objects.ReadAsync(entry, "evidence/plain.txt").ConfigureAwait(false)).Verified, "identity whole read verified");
        }

        private async Task TamperedAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            string id = await CaptureAsync(bb, t).ConfigureAwait(false);
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
            foreach (bool compress in new[] { true, false })
            {
                string name = compress ? "evidence/tamper-gz.txt" : "evidence/tamper-plain.txt";
                BlackBoxObject obj = await bb.Objects.WriteAsync(entry, name, "text/plain", Encoding.UTF8.GetBytes("trustworthy evidence " + name), compress).ConfigureAwait(false);
                byte[] stored = await StoredAsync(bb, entry, obj).ConfigureAwait(false);
                stored[stored.Length - 1] ^= 0x5A;
                if (Target == BlackBoxStorageTargetEnum.Database)
                {
                    await t.Driver.BlackBoxObjects.WriteDataAsync(obj.Id, stored).ConfigureAwait(false);
                }
                else
                {
                    BlackBoxObjectRef reference = BlackBoxObjectRepository.RefOf(entry, obj);
                    await bb.Stores.Get(Target).WriteAsync(new BlackBoxObjectWrite { Reference = reference, ContentType = "text/plain", ContentEncoding = obj.ContentEncoding, Data = stored }).ConfigureAwait(false);
                }
                await AssertThrowsAsync<BlackBoxObjectCorruptException>(() => bb.Objects.ReadAsync(entry, name), name + " tampered reads corrupt").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxObjectCorruptException>(() => bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, name), name + " through the service").ConfigureAwait(false);
            }
        }

        private async Task DeletedOutOfBandAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            string id = await CaptureAsync(bb, t).ConfigureAwait(false);
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
            BlackBoxObject obj = (await t.Driver.BlackBoxObjects.ReadAsync(id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false))!;
            if (Target == BlackBoxStorageTargetEnum.Database) await t.Driver.BlackBoxObjects.WriteDataAsync(obj.Id, null).ConfigureAwait(false);
            else await bb.Stores.Get(Target).DeleteAsync(BlackBoxObjectRepository.RefOf(entry, obj)).ConfigureAwait(false);

            await AssertThrowsAsync<BlackBoxObjectMissingException>(() => bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput), "read says missing (410)").ConfigureAwait(false);
            BlackBoxGcReport report = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
            AssertTrue(report.ObjectsMissing >= 1, "reported missing: " + report.ObjectsMissing);
            AssertTrue((await t.Driver.BlackBoxObjects.ReadByIdAsync(obj.Id).ConfigureAwait(false))!.IsMissing, "object marked missing");
            AssertEqual(BlackBoxStateEnum.ObjectMissing, (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!.State, "entry ObjectMissing");
            AssertNotNull(await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false), "the rest stays readable");
            BlackBoxGcReport again = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
            AssertEqual(0, again.ObjectsMissing, "marked once");
        }

        private async Task GcDeletesAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
            string old = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
            await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(old).ConfigureAwait(false))!;
            List<BlackBoxObject> objects = await ObjectsAsync(t, old).ConfigureAwait(false);
            List<BlackBoxObjectRef> refs = objects.Select(o => BlackBoxObjectRepository.RefOf(entry, o)).ToList();
            await t.Driver.BlackBoxEntries.UpdateExpiresAsync(old, DateTime.UtcNow.AddDays(-1)).ConfigureAwait(false);
            BlackBoxGcReport report = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
            AssertEqual(1, report.Expired, "expired");
            AssertNull(await t.Driver.BlackBoxEntries.ReadAsync(old, null, true).ConfigureAwait(false), "entry row gone");
            AssertEqual(0L, (await t.Driver.BlackBoxObjects.EnumerateAsync(old, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "object rows gone");
            if (Target != BlackBoxStorageTargetEnum.Database)
            {
                foreach (BlackBoxObjectRef reference in refs) AssertFalse(await bb.Stores.Get(Target).ExistsAsync(reference).ConfigureAwait(false), "store bytes gone: " + reference.StorageKey);
            }
        }

        private async Task MigrationAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            BlackBoxStorageTargetEnum to = MigrationTarget;
            ArmadaSettings settings = await SettingsAsync(to).ConfigureAwait(false);
            settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Database;
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            List<string> ids = new List<string>();
            for (int i = 0; i < 3; i++) ids.Add(await CaptureAsync(bb, t).ConfigureAwait(false));
            Dictionary<string, string> before = new Dictionary<string, string>();
            foreach (string id in ids) before[id] = Encoding.UTF8.GetString((await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false)).Data);

            // Interrupt after the first entry: the job stays Running with its cursor saved.
            BlackBoxStorageMigrationJob job = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = to, BatchSize = 2 }, false).ConfigureAwait(false);
            AssertEqual(3L, job.TotalEntries, "total");
            AssertStartsWith("bbm_", job.Id, "id prefix");
            using (CancellationTokenSource interrupt = new CancellationTokenSource())
            {
                bb.Migrator.OnEntryProcessedAsync = (entryId, ct) => { interrupt.Cancel(); return Task.CompletedTask; };
                await AssertThrowsAsync<OperationCanceledException>(() => bb.Migrator.RunAsync(job.Id, interrupt.Token), "interrupted").ConfigureAwait(false);
            }
            bb.Migrator.OnEntryProcessedAsync = null;
            BlackBoxStorageMigrationJob paused = (await bb.Service.GetStorageMigrationAsync(BlackBoxTestKit.Admin(), job.Id).ConfigureAwait(false));
            AssertEqual(BlackBoxJobStateEnum.Running, paused.State, "left Running");
            AssertEqual(1L, paused.MigratedEntries, "one moved before the interruption");
            AssertTrue(paused.LastEntryId != null && ids.Contains(paused.LastEntryId), "cursor saved: " + paused.LastEntryId);
            foreach (string id in ids) AssertEqual(before[id], Encoding.UTF8.GetString((await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false)).Data), "readable mid-migration " + id);

            // Starting the same move again returns the unfinished job (idempotent); running it finishes the rest.
            BlackBoxStorageMigrationJob again = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = to }, false).ConfigureAwait(false);
            AssertEqual(job.Id, again.Id, "same job");
            BlackBoxStorageMigrationJob done = await bb.Migrator.RunAsync(job.Id).ConfigureAwait(false);
            AssertEqual(BlackBoxJobStateEnum.Completed, done.State, "completed: " + done.LastError);
            AssertEqual(3L, done.MigratedEntries, "all moved");
            AssertEqual(0L, done.FailedEntries, "none failed");
            AssertTrue(done.BytesCopied > 0, "bytes copied");
            foreach (string id in ids)
            {
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(to, entry.StorageTarget, "switched " + id);
                AssertNotNull(entry.StoragePrefix, "prefix " + id);
                foreach (BlackBoxObject obj in await ObjectsAsync(t, id).ConfigureAwait(false))
                {
                    AssertNotNull(obj.StorageKey, "keyed " + obj.Name);
                    AssertNull((await t.Driver.BlackBoxObjects.ReadByIdAsync(obj.Id, true).ConfigureAwait(false))!.Data, "source deleted " + obj.Name);
                    AssertTrue(await bb.Stores.Get(to).ExistsAsync(BlackBoxObjectRepository.RefOf(entry, obj)).ConfigureAwait(false), "on the target " + obj.Name);
                }
                BlackBoxObjectReadResult read = await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false);
                AssertTrue(read.Verified, "verified after the move");
                AssertEqual(before[id], Encoding.UTF8.GetString(read.Data), "same bytes " + id);
            }
            BlackBoxStorageMigrationJob noop = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = to }, false).ConfigureAwait(false);
            AssertEqual(0L, noop.TotalEntries, "a second migration finds nothing to move");
            AssertEqual(BlackBoxJobStateEnum.Completed, noop.State, "and is complete at once");

            // And back to the database.
            BlackBoxStorageMigrationJob back = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { From = to, To = BlackBoxStorageTargetEnum.Database }, false).ConfigureAwait(false);
            BlackBoxStorageMigrationJob backDone = await bb.Migrator.RunAsync(back.Id).ConfigureAwait(false);
            AssertEqual(BlackBoxJobStateEnum.Completed, backDone.State, "back: " + backDone.LastError);
            foreach (string id in ids)
            {
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Database, entry.StorageTarget, "back on the database " + id);
                AssertEqual(before[id], Encoding.UTF8.GetString((await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false)).Data), "same bytes back " + id);
            }
        }

        private async Task StoreOperationsAsync()
        {
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            IBlackBoxStore store = bb.Stores.Get(Target);
            AssertEqual(Target, store.Target, "target");
            BlackBoxStoreHealth health = await store.CheckHealthAsync().ConfigureAwait(false);
            AssertTrue(health.Healthy, "healthy: " + health.Message);
            string prefix = settings.BlackBox.Storage.KeyPrefix + "/ops";
            BlackBoxObjectRef a = new BlackBoxObjectRef { Target = Target, Name = "a.txt", StorageKey = prefix + "/a.txt" };
            BlackBoxObjectRef b = new BlackBoxObjectRef { Target = Target, Name = "evidence/b.bin", StorageKey = prefix + "/evidence/b.bin" };
            byte[] bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
            await store.WriteAsync(new BlackBoxObjectWrite { Reference = a, ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("hello store") }).ConfigureAwait(false);
            await store.WriteAsync(new BlackBoxObjectWrite { Reference = b, ContentType = "application/octet-stream", Data = bytes }).ConfigureAwait(false);
            AssertTrue(await store.ExistsAsync(a).ConfigureAwait(false), "a exists");
            AssertEqual("store", Read(await store.OpenReadAsync(a, 6, 5).ConfigureAwait(false)), "ranged");
            AssertEqual("hello store", Read(await store.OpenReadAsync(a, 0, null).ConfigureAwait(false)), "whole");
            AssertEqual("", Read(await store.OpenReadAsync(a, 500, 10).ConfigureAwait(false)), "past the end");
            byte[] slice = ReadBytes(await store.OpenReadAsync(b, 4000, 2000).ConfigureAwait(false));
            AssertEqual(1000, slice.Length, "clipped range");
            AssertTrue(slice.SequenceEqual(bytes.Skip(4000)), "binary range");
            await store.WriteAsync(new BlackBoxObjectWrite { Reference = a, ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("replaced") }).ConfigureAwait(false);
            AssertEqual("replaced", Read(await store.OpenReadAsync(a, 0, null).ConfigureAwait(false)), "overwrite");
            BlackBoxObjectRef missing = new BlackBoxObjectRef { Target = Target, Name = "missing.txt", StorageKey = prefix + "/missing.txt" };
            await AssertThrowsAsync<BlackBoxObjectMissingException>(() => store.OpenReadAsync(missing, 0, null), "missing reads as missing").ConfigureAwait(false);
            AssertFalse(await store.ExistsAsync(missing).ConfigureAwait(false), "missing does not exist");
            await store.DeleteAsync(missing).ConfigureAwait(false);
            BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = new List<BlackBoxObjectRef> { a, b, missing } }).ConfigureAwait(false);
            AssertEqual(3, deleted.Requested, "requested");
            AssertEqual(0, deleted.Failed, "no failures: " + deleted.FirstError);
            AssertEqual(3, deleted.Deleted, "deleting a missing key succeeds");
            AssertFalse(await store.ExistsAsync(a).ConfigureAwait(false), "a gone");
            AssertFalse(await store.ExistsAsync(b).ConfigureAwait(false), "b gone");
            await AssertThrowsAsync<ArgumentException>(() => store.WriteAsync(new BlackBoxObjectWrite { Reference = new BlackBoxObjectRef { Target = Target, Name = "nokey" }, Data = new byte[] { 1 } }), "a key is required").ConfigureAwait(false);
        }

        private async Task StoreEnumerateAsync()
        {
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            IBlackBoxStore store = bb.Stores.Get(Target);
            string prefix = settings.BlackBox.Storage.KeyPrefix;
            string[] keys = { prefix + "/t/v/2026/01/bbx_1/record.json", prefix + "/t/v/2026/01/bbx_1/evidence/a.txt", prefix + "/t/v/2026/02/bbx_2/record.json" };
            foreach (string key in keys) await store.WriteAsync(new BlackBoxObjectWrite { Reference = new BlackBoxObjectRef { Target = Target, Name = key, StorageKey = key }, ContentType = "text/plain", Data = Encoding.UTF8.GetBytes(key) }).ConfigureAwait(false);
            List<BlackBoxStoredKey> all = new List<BlackBoxStoredKey>();
            await foreach (BlackBoxStoredKey key in store.EnumerateAsync(prefix + "/").ConfigureAwait(false)) all.Add(key);
            AssertEqual(String.Join(",", keys.OrderBy(k => k, StringComparer.Ordinal)), String.Join(",", all.Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal)), "every key, no folders");
            foreach (BlackBoxStoredKey key in all)
            {
                AssertEqual((long)Encoding.UTF8.GetByteCount(key.Key), key.SizeBytes, "size of " + key.Key);
                AssertNotNull(key.LastModifiedUtc, "time of " + key.Key);
            }
            List<BlackBoxStoredKey> one = new List<BlackBoxStoredKey>();
            await foreach (BlackBoxStoredKey key in store.EnumerateAsync(prefix + "/t/v/2026/01/bbx_1/").ConfigureAwait(false)) one.Add(key);
            AssertEqual(2, one.Count, "prefix narrows");
        }

        private async Task OrphanSweepAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            string id = await CaptureAsync(bb, t).ConfigureAwait(false);
            IBlackBoxStore store = bb.Stores.Get(Target);
            BlackBoxObjectRef stray = new BlackBoxObjectRef { Target = Target, Name = "stray", StorageKey = settings.BlackBox.Storage.KeyPrefix + "/_/_/2020/01/bbx_gone/record.json" };
            await store.WriteAsync(new BlackBoxObjectWrite { Reference = stray, ContentType = "application/json", Data = Encoding.UTF8.GetBytes("{}") }).ConfigureAwait(false);

            BlackBoxGcReport young = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
            AssertEqual(0, young.OrphansRemoved, "an orphan younger than the grace period is kept");
            AssertTrue(await store.ExistsAsync(stray).ConfigureAwait(false), "still there");

            bb.GarbageCollector.Clock = () => DateTime.UtcNow.AddDays(30);
            BlackBoxGcReport dry = await bb.GarbageCollector.RunAsync(true).ConfigureAwait(false);
            AssertEqual(1, dry.OrphansRemoved, "dry run counts the orphan");
            AssertTrue(await store.ExistsAsync(stray).ConfigureAwait(false), "dry run deletes nothing");
            BlackBoxGcReport old = await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
            AssertEqual(1, old.OrphansRemoved, "the old orphan is removed: " + String.Join("; ", old.Errors));
            AssertFalse(await store.ExistsAsync(stray).ConfigureAwait(false), "deleted");
            AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false), "the referenced entry stays");
            BlackBoxObjectReadResult read = await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.Record).ConfigureAwait(false);
            AssertTrue(read.Data.Length > 0, "referenced objects stay");
        }

        private async Task TargetChangeAsync()
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = await SettingsAsync(Target).ConfigureAwait(false);
            settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Database;
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            string onDatabase = await CaptureAsync(bb, t).ConfigureAwait(false);
            settings.BlackBox.Storage.Target = Target;
            await bb.ApplySettingsChanged(settings.BlackBox.Retention.Clone()).ConfigureAwait(false);
            string onTarget = await CaptureAsync(bb, t).ConfigureAwait(false);
            AssertEqual(BlackBoxStorageTargetEnum.Database, (await t.Driver.BlackBoxEntries.ReadAsync(onDatabase).ConfigureAwait(false))!.StorageTarget, "old entry stays on the database");
            AssertEqual(Target, (await t.Driver.BlackBoxEntries.ReadAsync(onTarget).ConfigureAwait(false))!.StorageTarget, "new entry on the target");
            foreach (string id in new[] { onDatabase, onTarget })
            {
                BlackBoxEntry entry = await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false);
                AssertNotNull(entry.Record, "record of " + id);
                AssertContains("agent output line", Encoding.UTF8.GetString((await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false)).Data), "evidence of " + id);
            }
            BlackBoxStorageStatus status = await bb.Service.GetStorageStatusAsync(BlackBoxTestKit.Admin()).ConfigureAwait(false);
            AssertEqual(Target, status.Target, "status target");
            AssertTrue(status.Health.Healthy, "status healthy: " + status.Health.Message);
            AssertEqual(2, status.Usage.Count, "usage on both targets");
        }

        #endregion

        #region Helpers

        private static string Read(Stream stream)
        {
            return Encoding.UTF8.GetString(ReadBytes(stream));
        }

        private static byte[] ReadBytes(Stream stream)
        {
            using (stream)
            using (MemoryStream buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        #endregion
    }
}
