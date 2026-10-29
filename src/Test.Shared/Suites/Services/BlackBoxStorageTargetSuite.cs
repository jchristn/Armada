namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box storage targets without emulators (plan section 16.4): settings validation for every target, masked
    /// credentials, the Disk root and symlink guards, unreachable and misconfigured stores with and without the database
    /// fallback, oversized evidence, the doctor check, migration validation and failure handling, resume after a
    /// restart, the backup inventory, storage status, metrics and applying a settings change in place.
    /// </summary>
    public sealed class BlackBoxStorageTargetSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxStorageTargets";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("settings_validation", "Each target names its missing or invalid settings; valid settings pass; an unknown target is refused", TestTags.Negative, () =>
            {
                BlackBoxStorageSettings s = new BlackBoxStorageSettings();
                AssertEqual(0, BlackBoxStorageSettingsValidator.Validate(s).Count, "Database needs nothing");
                AssertEqual(0, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Disk).Count, "Disk defaults to the data directory");
                s.Disk.Directory = "bad\0dir";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Disk).Count == 1, "invalid disk path");

                List<string> s3 = BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3);
                AssertTrue(s3.Any(e => e.Contains("blackBox.storage.s3.bucket is required")), "missing bucket: " + String.Join(" | ", s3));
                s.S3.Bucket = "armada"; s.S3.Endpoint = "ftp://nope"; s.S3.AccessKey = "AKIAEXAMPLE";
                s3 = BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3);
                AssertTrue(s3.Any(e => e.Contains("endpoint must be an http or https URL")), "bad endpoint");
                AssertTrue(s3.Any(e => e.Contains("both be set or both be empty")), "half the credentials");
                AssertFalse(String.Join(" ", s3).Contains("AKIAEXAMPLE"), "no secret in the messages");
                s.S3.Endpoint = "https://minio.local:9000"; s.S3.UseSsl = false; s.S3.SecretKey = "secret";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3).Any(e => e.Contains("useSsl is false")), "scheme and useSsl disagree");
                s.S3.UseSsl = true;
                AssertEqual(0, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3).Count, "valid S3-compatible");
                s.S3.Endpoint = "localhost:9000";
                AssertEqual(0, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3).Count, "an endpoint without a scheme uses useSsl");
                s.S3.Bucket = "a/b";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.S3).Any(e => e.Contains("bucket must be")), "bad bucket");

                List<string> azure = BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.AzureBlob);
                AssertTrue(azure.Any(e => e.Contains("container is required")) && azure.Any(e => e.Contains("accountName is required")) && azure.Any(e => e.Contains("accessKey is required")), "azure: " + String.Join(" | ", azure));
                s.AzureBlob.Container = "c"; s.AzureBlob.ConnectionString = "AccountName=a;Foo=b";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.AzureBlob).Any(e => e.Contains("must contain AccountName and AccountKey")), "bad connection string");
                s.AzureBlob.ConnectionString = "DefaultEndpointsProtocol=http;AccountName=a;AccountKey=k;BlobEndpoint=http://127.0.0.1:10000/a";
                AssertEqual(0, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.AzureBlob).Count, "connection string");

                List<string> gcs = BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.GoogleCloud);
                AssertEqual(3, gcs.Count, "project, bucket, credentials: " + String.Join(" | ", gcs));
                s.GoogleCloud.ProjectId = "p"; s.GoogleCloud.Bucket = "b"; s.GoogleCloud.CredentialsPath = Path.Combine(Path.GetTempPath(), "armada-no-such-" + Guid.NewGuid().ToString("N") + ".json");
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.GoogleCloud).Single().Contains("does not exist"), "credentials file missing");

                AssertEqual(4, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Cifs).Count, "cifs host, share, user, password");
                s.Cifs.Hostname = "bad host name!"; s.Cifs.Share = "s"; s.Cifs.Username = "u"; s.Cifs.Password = "p";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Cifs).Single().Contains("not a valid host"), "bad cifs host");
                AssertEqual(2, BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Nfs).Count, "nfs host and export");
                s.Nfs.Hostname = "nfs.local"; s.Nfs.ExportPath = "export";
                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Nfs).Single().Contains("absolute export path"), "relative export");

                AssertTrue(BlackBoxStorageSettingsValidator.Validate(s, BlackBoxStorageTargetEnum.Unknown).Single().Contains("Unknown black box storage target"), "unknown target");
                AssertThrows<ArgumentException>(() => BlackBoxStorageSettingsValidator.Require(s, BlackBoxStorageTargetEnum.Nfs), "Require throws");
                s.Nfs.Port = 0; s.Nfs.Version = 9; s.Cifs.Port = 70000;
                AssertEqual(1, s.Nfs.Port, "port clamped"); AssertEqual(4, s.Nfs.Version, "version clamped"); AssertEqual(65535, s.Cifs.Port, "cifs port clamped");
                return Task.CompletedTask;
            }));

            cases.Add(Case("credentials_masked", "Every storage secret reads back masked and is kept when written back masked; endpoints and ports are not secrets", TestTags.Negative, () =>
            {
                BlackBoxSettings settings = new BlackBoxSettings();
                BlackBoxStorageSettings s = settings.Storage;
                s.S3.AccessKey = "AKIA1"; s.S3.SecretKey = "s3secret"; s.S3.Endpoint = "http://minio:9000";
                s.AzureBlob.AccessKey = "azkey"; s.AzureBlob.ConnectionString = "AccountName=a;AccountKey=azkey"; s.AzureBlob.Endpoint = "http://azurite:10000/a/";
                s.GoogleCloud.CredentialsPath = "/secret/creds.json"; s.GoogleCloud.Endpoint = "http://gcs:4443/storage/v1/";
                s.Cifs.Password = "cifspass"; s.Cifs.Port = 4445; s.Nfs.Port = 2049;
                BlackBoxSettings masked = BlackBoxSettingsSecrets.Mask(settings, "********");
                string json = System.Text.Json.JsonSerializer.Serialize(masked);
                foreach (string secret in new[] { "AKIA1", "s3secret", "azkey", "/secret/creds.json", "cifspass" }) AssertFalse(json.Contains(secret), secret + " masked");
                AssertEqual("http://minio:9000", masked.Storage.S3.Endpoint, "endpoint kept");
                AssertEqual("http://azurite:10000/a/", masked.Storage.AzureBlob.Endpoint, "azure endpoint kept");
                AssertEqual(4445, masked.Storage.Cifs.Port, "port kept");
                AssertEqual("s3secret", settings.Storage.S3.SecretKey, "source untouched");
                BlackBoxSettingsSecrets.KeepMasked(masked, settings, "********");
                AssertEqual("s3secret", masked.Storage.S3.SecretKey, "written back masked keeps the secret");
                AssertEqual("cifspass", masked.Storage.Cifs.Password, "cifs kept");
                AssertEqual("/secret/creds.json", masked.Storage.GoogleCloud.CredentialsPath, "credentials path kept");
                string?[] values = BlackBoxRuntime.SecretValues(settings);
                AssertTrue(values.Contains("s3secret") && values.Contains("cifspass") && values.Contains("azkey"), "secrets registered with the redactor");
                return Task.CompletedTask;
            }));

            cases.Add(Case("disk_root_and_symlinks", "The Disk store refuses keys outside its root and keys whose path crosses a symbolic link", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("bbroot");
                string outside = TestTemp.NewDirectory("bboutside");
                File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside the root");
                using BlobjectBlackBoxStore store = new BlobjectBlackBoxStore(BlackBoxStorageTargetEnum.Disk, new Blobject.Disk.DiskBlobClient(new Blobject.Disk.DiskSettings(root)), root);
                foreach (string key in new[] { "../escape.txt", "a/../../escape.txt", "/etc/passwd", "a/./b", "C:/windows/x" })
                {
                    BlackBoxObjectRef reference = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = key, StorageKey = key };
                    await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => store.WriteAsync(new BlackBoxObjectWrite { Reference = reference, Data = new byte[] { 1 } }), "write refused: " + key).ConfigureAwait(false);
                    await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => store.OpenReadAsync(reference, 0, null), "read refused: " + key).ConfigureAwait(false);
                }
                AssertFalse(File.Exists(Path.Combine(Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!, "escape.txt")), "nothing written outside");

                Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
                BlackBoxObjectRef viaDir = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "x", StorageKey = "linked/secret.txt" };
                await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => store.OpenReadAsync(viaDir, 0, null), "read through a symlinked directory").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => store.WriteAsync(new BlackBoxObjectWrite { Reference = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "y", StorageKey = "linked/new.txt" }, Data = new byte[] { 1 } }), "write through a symlinked directory").ConfigureAwait(false);
                AssertFalse(File.Exists(Path.Combine(outside, "new.txt")), "nothing written through the link");
                File.CreateSymbolicLink(Path.Combine(root, "file-link.txt"), Path.Combine(outside, "secret.txt"));
                BlackBoxObjectRef viaFile = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "z", StorageKey = "file-link.txt" };
                await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => store.OpenReadAsync(viaFile, 0, null), "read of a symlinked file").ConfigureAwait(false);
                AssertFalse(await ThrowsOrFalseAsync(() => store.ExistsAsync(viaFile)).ConfigureAwait(false), "exists does not follow the link");
                BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = new List<BlackBoxObjectRef> { viaFile, viaDir } }).ConfigureAwait(false);
                AssertEqual(2, deleted.Failed, "deletes through links refused");
                AssertTrue(File.Exists(Path.Combine(outside, "secret.txt")), "the target of the link survives");
                List<BlackBoxStoredKey> listed = new List<BlackBoxStoredKey>();
                await foreach (BlackBoxStoredKey k in store.EnumerateAsync("").ConfigureAwait(false)) listed.Add(k);
                AssertFalse(listed.Any(k => k.Key.StartsWith("linked", StringComparison.Ordinal) || k.Key == "file-link.txt"), "listing skips links");

                BlackBoxObjectRef ok = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Disk, Name = "fine", StorageKey = "armada/blackbox/t/v/2026/01/bbx_1/evidence/fine.txt" };
                await store.WriteAsync(new BlackBoxObjectWrite { Reference = ok, Data = Encoding.UTF8.GetBytes("inside") }).ConfigureAwait(false);
                AssertTrue(File.Exists(Path.Combine(root, "armada", "blackbox", "t", "v", "2026", "01", "bbx_1", "evidence", "fine.txt")), "keys map under the root");
            }));

            cases.Add(Case("unreachable_with_fallback", "An unreachable S3 store with fallback on writes the entry to the database and emits blackbox.storage_fallback", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = DeadS3();
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
                BlackBoxStoreHealth health = await bb.Stores.Get(BlackBoxStorageTargetEnum.S3).CheckHealthAsync().ConfigureAwait(false);
                AssertFalse(health.Healthy, "unhealthy");
                string id = await CaptureAsync(bb, t).ConfigureAwait(false);
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Database, entry.StorageTarget, "moved to the database");
                AssertNull(entry.StoragePrefix, "no prefix");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.StorageFallback), "fallback event");
                BlackBoxStorageFallbackPayload payload = (BlackBoxStorageFallbackPayload)sink.Events.First(e => e.EventType == BlackBoxEventTypes.StorageFallback).Payload!;
                AssertEqual(BlackBoxStorageTargetEnum.S3, payload.From, "from"); AssertEqual(BlackBoxStorageTargetEnum.Database, payload.To, "to");
                AssertFalse((payload.Error ?? "").Contains(settings.BlackBox.Storage.S3.SecretKey!), "no secret in the event");
                List<BlackBoxObject> objects = (await t.Driver.BlackBoxObjects.EnumerateAsync(id, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects;
                AssertTrue(objects.Count >= 5 && objects.All(o => o.StorageKey == null && !o.IsMissing), "every object in the database");
                AssertContains("agent output", Encoding.UTF8.GetString((await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput).ConfigureAwait(false)).Data), "readable");
                BlackBoxRecord record = (await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false)).Record!;
                AssertTrue(record.Truncation.Notes.Any(n => n.Contains("written to the database instead")), "the record says so");

                // A misconfigured target (no bucket) is a store failure too.
                settings.BlackBox.Storage.S3.Bucket = null;
                bb.Stores.Reset();
                string second = await CaptureAsync(bb, t).ConfigureAwait(false);
                AssertEqual(BlackBoxStorageTargetEnum.Database, (await t.Driver.BlackBoxEntries.ReadAsync(second).ConfigureAwait(false))!.StorageTarget, "misconfigured falls back");
                AssertEqual(2, sink.Count(BlackBoxEventTypes.StorageFallback), "second fallback event");
            }));

            cases.Add(Case("unreachable_without_fallback", "With fallback off, record.json still goes to the database and the other objects are marked missing (entry ObjectMissing)", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.CaptureTimeoutMs = 30000;
                string blocker = TestTemp.NewFile("bbblock", ".txt");
                File.WriteAllText(blocker, "a file where the Disk directory should be");
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = Path.Combine(blocker, "blackbox");
                settings.BlackBox.Storage.FallbackToDatabase = false;
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings, sink);
                string id = await CaptureAsync(bb, t).ConfigureAwait(false);
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Disk, entry.StorageTarget, "stays on its target");
                AssertEqual(BlackBoxStateEnum.ObjectMissing, entry.State, "ObjectMissing");
                AssertEqual(0, sink.Count(BlackBoxEventTypes.StorageFallback), "no fallback event");
                BlackBoxObject record = (await t.Driver.BlackBoxObjects.ReadAsync(id, BlackBoxObjectNames.Record).ConfigureAwait(false))!;
                AssertNull(record.StorageKey, "record.json lives in the database");
                AssertFalse(record.IsMissing, "record.json present");
                AssertNotNull((await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false)).Record, "record readable");
                List<BlackBoxObject> others = (await t.Driver.BlackBoxObjects.EnumerateAsync(id, new EnumerationQuery { PageSize = 100 }).ConfigureAwait(false)).Objects.Where(o => o.Name != BlackBoxObjectNames.Record).ToList();
                AssertTrue(others.Count >= 4 && others.All(o => o.IsMissing), "evidence and briefs missing");
                await AssertThrowsAsync<BlackBoxObjectMissingException>(() => bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput), "evidence reads 410").ConfigureAwait(false);
            }));

            cases.Add(Case("oversized_evidence", "An evidence object over maxEvidenceObjectBytes keeps its head and tail on the target and records the original size", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbbig");
                settings.BlackBox.Storage.MaxEvidenceObjectBytes = 65536;
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.AgentOutput = "HEAD-MARKER\n" + new string('x', 300000) + "\nTAIL-MARKER";
                await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                BlackBoxObjectReadResult read = await bb.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.AgentOutput, 0, 1048576).ConfigureAwait(false);
                string text = Encoding.UTF8.GetString(read.Data);
                AssertTrue(read.TotalBytes <= 65536, "capped: " + read.TotalBytes);
                AssertTrue(text.StartsWith("HEAD-MARKER", StringComparison.Ordinal) && text.EndsWith("TAIL-MARKER", StringComparison.Ordinal), "head and tail kept");
                AssertContains("cut by Armada", text, "marker line");
                BlackBoxRecord record = (await bb.Service.GetEntryAsync(BlackBoxTestKit.Admin(), id).ConfigureAwait(false)).Record!;
                BlackBoxTruncatedField field = record.Truncation.Fields.Single(f => f.Field == BlackBoxObjectNames.AgentOutput);
                AssertTrue(field.OriginalSize > 300000, "original size recorded");
                AssertEqual(BlackBoxStorageTargetEnum.Disk, (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!.StorageTarget, "on Disk");
            }));

            cases.Add(Case("doctor_check", "The storage doctor check passes for a healthy target and fails with the reason for a misconfigured or unreachable one", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                BlackBoxStorageDoctorResult database = await BlackBoxStorageDoctor.CheckAsync(settings.BlackBox, bb.Stores).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Pass, database.Status, "database: " + database.Message);
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbdoctor");
                bb.Stores.Reset();
                AssertEqual(DoctorCheckStatusEnum.Pass, (await BlackBoxStorageDoctor.CheckAsync(settings.BlackBox, bb.Stores).ConfigureAwait(false)).Status, "disk");
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.S3;
                BlackBoxStorageDoctorResult missing = await BlackBoxStorageDoctor.CheckAsync(settings.BlackBox, bb.Stores).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Fail, missing.Status, "misconfigured");
                AssertContains("blackBox.storage.s3.bucket is required", missing.Message, "names the setting");
                AssertContains("fall back to the database", missing.Message, "says what happens");
                ArmadaSettings dead = DeadS3();
                using BlackBoxRuntime deadRuntime = BlackBoxTestKit.Runtime(t.Driver, dead);
                BlackBoxStorageDoctorResult unreachable = await BlackBoxStorageDoctor.CheckAsync(dead.BlackBox, deadRuntime.Stores).ConfigureAwait(false);
                AssertEqual(DoctorCheckStatusEnum.Fail, unreachable.Status, "unreachable: " + unreachable.Message);
                AssertFalse(unreachable.Message.Contains(dead.BlackBox.Storage.S3.SecretKey!), "no secret");
                dead.BlackBox.Enabled = false;
                AssertEqual(DoctorCheckStatusEnum.Warn, (await BlackBoxStorageDoctor.CheckAsync(dead.BlackBox, deadRuntime.Stores).ConfigureAwait(false)).Status, "disabled warns");
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Database;
                AssertEqual(DoctorCheckStatusEnum.Warn, (await BlackBoxStorageDoctor.CheckAsync(settings.BlackBox, null).ConfigureAwait(false)).Status, "no runtime warns");
                BlackBoxStorageStatus status = await deadRuntime.Service.GetStorageStatusAsync(BlackBoxTestKit.Admin()).ConfigureAwait(false);
                AssertFalse(status.Health.Healthy, "storage status reports the unhealthy store");
            }));

            cases.Add(Case("migration_validation", "Migration needs an admin, a known, different, configured and healthy target; one job at a time; an unknown job is not found", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbmigv");
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                AuthContext admin = BlackBoxTestKit.Admin();
                await AssertThrowsAsync<UnauthorizedAccessException>(() => bb.Service.StartStorageMigrationAsync(BlackBoxTestKit.User("ten_a", "usr_a", true), new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk }), "tenant admin refused").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => bb.Service.StartStorageMigrationAsync(admin, new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Unknown }), "unknown target").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => bb.Service.StartStorageMigrationAsync(admin, new BlackBoxStorageMigrationRequest { From = BlackBoxStorageTargetEnum.Disk, To = BlackBoxStorageTargetEnum.Disk }), "same target").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => bb.Service.StartStorageMigrationAsync(admin, new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.S3 }), "misconfigured target").ConfigureAwait(false);
                ArmadaSettings dead = DeadS3();
                using BlackBoxRuntime deadRuntime = BlackBoxTestKit.Runtime(t.Driver, dead);
                await AssertThrowsAsync<BlackBoxStoreUnavailableException>(() => deadRuntime.Service.StartStorageMigrationAsync(admin, new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.S3 }), "unhealthy target").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => bb.Service.GetStorageMigrationAsync(admin, "bbm_none"), "unknown job").ConfigureAwait(false);

                await CaptureAsync(bb, t).ConfigureAwait(false);
                BlackBoxStorageMigrationJob pending = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk }, false).ConfigureAwait(false);
                AssertEqual(BlackBoxJobStateEnum.Pending, pending.State, "pending");
                await AssertThrowsAsync<InvalidOperationException>(() => bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { From = BlackBoxStorageTargetEnum.Disk, To = BlackBoxStorageTargetEnum.Database }, false), "a different migration while one is unfinished").ConfigureAwait(false);
                BlackBoxStorageStatus status = await bb.Service.GetStorageStatusAsync(admin).ConfigureAwait(false);
                AssertEqual(pending.Id, status.ActiveMigrationId, "status names the unfinished job");
                BlackBoxStorageMigrationJob viaService = await bb.Service.StartStorageMigrationAsync(admin, new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk }).ConfigureAwait(false);
                AssertEqual(pending.Id, viaService.Id, "the service returns the same job and runs it in the background");
                BlackBoxStorageMigrationJob finished = await WaitAsync(bb, pending.Id).ConfigureAwait(false);
                AssertEqual(BlackBoxJobStateEnum.Completed, finished.State, "background run completes: " + finished.LastError);
                AssertEqual(1L, finished.MigratedEntries, "moved");
            }));

            cases.Add(Case("migration_failures_and_restart", "A corrupt source object fails only its entry (not switched); an interrupted job resumes after a restart", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbmigf");
                string good;
                string bad;
                string jobId;
                using (BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings))
                {
                    good = await CaptureAsync(bb, t).ConfigureAwait(false);
                    bad = await CaptureAsync(bb, t).ConfigureAwait(false);
                    BlackBoxObject tampered = (await t.Driver.BlackBoxObjects.ReadAsync(bad, BlackBoxObjectNames.AgentOutput, true).ConfigureAwait(false))!;
                    tampered.Data![0] ^= 0x01;
                    await t.Driver.BlackBoxObjects.WriteDataAsync(tampered.Id, tampered.Data).ConfigureAwait(false);
                    BlackBoxStorageMigrationJob job = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk, BatchSize = 1 }, false).ConfigureAwait(false);
                    jobId = job.Id;
                    using CancellationTokenSource interrupt = new CancellationTokenSource();
                    bb.Migrator.OnEntryProcessedAsync = (entryId, ct) => { interrupt.Cancel(); return Task.CompletedTask; };
                    await AssertThrowsAsync<OperationCanceledException>(() => bb.Migrator.RunAsync(job.Id, interrupt.Token), "interrupted").ConfigureAwait(false);
                }

                // "Restart": a new runtime resumes the unfinished job from its cursor in the background.
                using (BlackBoxRuntime restarted = BlackBoxTestKit.Runtime(t.Driver, settings))
                {
                    await restarted.Migrator.ResumePendingAsync().ConfigureAwait(false);
                    BlackBoxStorageMigrationJob done = (await restarted.Migrator.GetAsync(jobId).ConfigureAwait(false))!;
                    AssertEqual(BlackBoxJobStateEnum.Failed, done.State, "finished with a failure");
                    AssertEqual(1L, done.MigratedEntries, "the good entry moved");
                    AssertEqual(1L, done.FailedEntries, "the corrupt entry failed");
                    AssertContains(bad, done.LastError ?? "", "the error names the entry");
                    AssertNotNull(done.FinishedUtc, "finished");
                    AssertEqual(BlackBoxStorageTargetEnum.Disk, (await t.Driver.BlackBoxEntries.ReadAsync(good).ConfigureAwait(false))!.StorageTarget, "good switched");
                    BlackBoxEntry badEntry = (await t.Driver.BlackBoxEntries.ReadAsync(bad).ConfigureAwait(false))!;
                    AssertEqual(BlackBoxStorageTargetEnum.Database, badEntry.StorageTarget, "corrupt entry not switched");
                    await AssertThrowsAsync<BlackBoxObjectCorruptException>(() => restarted.Service.ReadObjectAsync(BlackBoxTestKit.Admin(), bad, BlackBoxObjectNames.AgentOutput), "still corrupt where it was").ConfigureAwait(false);
                    AssertNotNull(await restarted.Service.GetEntryAsync(BlackBoxTestKit.Admin(), bad).ConfigureAwait(false), "the rest of the corrupt entry is readable");
                }
            }));

            cases.Add(Case("migration_and_writer_coordinate", "A migration waits for an entry write in flight and a write waits for a migration of the same entry; a write with an entry read before the move lands on the new target and never reverts the switch", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbmigrace");
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                BlackBoxEntryLocks locks = new BlackBoxEntryLocks();
                bb.Writer.EntryLocks = locks;
                bb.Migrator.EntryLocks = locks;
                string id = await CaptureAsync(bb, t).ConfigureAwait(false);
                await bb.Queue.DrainAsync().ConfigureAwait(false);
                BlackBoxEntry stale = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Database, stale.StorageTarget, "starts in the database");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(stale).ConfigureAwait(false);

                // A write in flight (the entry's lock held) holds the migration of that entry back.
                Task<BlackBoxStorageMigrationJob> run;
                using (await locks.AcquireAsync(id).ConfigureAwait(false))
                {
                    BlackBoxStorageMigrationJob job = await bb.Migrator.StartAsync(new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk }, false).ConfigureAwait(false);
                    run = bb.Migrator.RunAsync(job.Id);
                    await Task.Delay(300).ConfigureAwait(false);
                    AssertFalse(run.IsCompleted, "the migration waits for the write in flight");
                }
                BlackBoxStorageMigrationJob done = await run.ConfigureAwait(false);
                AssertEqual(BlackBoxJobStateEnum.Completed, done.State, "migrated: " + done.LastError);
                AssertEqual(BlackBoxStorageTargetEnum.Disk, (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!.StorageTarget, "switched to Disk");

                // A migration in flight holds a write back.
                Task write;
                using (await locks.AcquireAsync(id).ConfigureAwait(false))
                {
                    write = bb.Writer.WriteAsync(stale, record, null, CancellationToken.None, CancellationToken.None);
                    await Task.Delay(300).ConfigureAwait(false);
                    AssertFalse(write.IsCompleted, "the write waits for the migration in flight");
                }
                await write.ConfigureAwait(false);

                // The write used an entry read before the move: it lands on Disk and keeps the switch.
                BlackBoxEntry after = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStorageTargetEnum.Disk, after.StorageTarget, "the stale write did not revert the switch");
                AssertNotNull(after.StoragePrefix, "prefix kept");
                BlackBoxObject recordRow = (await t.Driver.BlackBoxObjects.ReadAsync(id, BlackBoxObjectNames.Record, false).ConfigureAwait(false))!;
                AssertNotNull(recordRow.StorageKey, "record.json was rewritten to the Disk target, not the database");
                BlackBoxRecord reread = await bb.Pipeline.LoadRecordAsync(after).ConfigureAwait(false);
                AssertEqual(id, reread.EntryId, "record.json reads back from the new target");
                AssertEqual(0, locks.Count, "no lock is left behind");
            }));

            cases.Add(Case("backup_inventory", "The backup inventory counts database entries and lists external ones with their target and prefix", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                BlackBoxBackupInventory empty = await BlackBoxBackupInventoryBuilder.BuildAsync(t.Driver).ConfigureAwait(false);
                AssertEqual(0L, empty.DatabaseEntries + empty.ExternalEntries, "empty");
                await CaptureAsync(bb, t).ConfigureAwait(false);
                settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                settings.BlackBox.Storage.Disk.Directory = TestTemp.NewDirectory("bbbackup");
                bb.Stores.Reset();
                string external = await CaptureAsync(bb, t).ConfigureAwait(false);
                await CaptureAsync(bb, t).ConfigureAwait(false);
                BlackBoxBackupInventory inventory = await BlackBoxBackupInventoryBuilder.BuildAsync(t.Driver, 1).ConfigureAwait(false);
                AssertEqual(1L, inventory.DatabaseEntries, "database entries");
                AssertTrue(inventory.DatabaseBytes > 0, "database bytes");
                AssertEqual(2L, inventory.ExternalEntries, "external entries");
                AssertEqual(BlackBoxStorageTargetEnum.Disk, inventory.ExternalTargets.Single().Target, "by target");
                AssertEqual(1, inventory.ExternalEntryRefs.Count, "capped list");
                AssertTrue(inventory.ExternalEntryRefsTruncated, "truncation flagged");
                AssertNotNull(inventory.ExternalEntryRefs[0].StoragePrefix, "prefix listed");
                AssertContains("does not contain", inventory.Message, "message");
                BlackBoxBackupInventory full = await BlackBoxBackupInventoryBuilder.BuildAsync(t.Driver).ConfigureAwait(false);
                AssertTrue(full.ExternalEntryRefs.Any(r => r.EntryId == external), "listed");
                AssertFalse(full.ExternalEntryRefsTruncated, "complete");
            }));

            cases.Add(Case("metrics", "Captures count armada.blackbox.entries with phase, step, kind and triage; GC counts armada.blackbox.gc_deleted; the bytes gauge reports per target", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                List<string> seen = new List<string>();
                using MeterListener listener = new MeterListener();
                listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == ArmadaMetrics.MeterName && instrument.Name.StartsWith("armada.blackbox.", StringComparison.Ordinal)) l.EnableMeasurementEvents(instrument); };
                listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
                {
                    string tagText = String.Join(",", tags.ToArray().Select(tg => tg.Key + "=" + tg.Value));
                    lock (seen) seen.Add(instrument.Name + ":" + value + ":" + tagText);
                });
                listener.Start();
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string old = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
                await t.Driver.BlackBoxEntries.UpdateExpiresAsync(old, DateTime.UtcNow.AddDays(-1)).ConfigureAwait(false);
                await bb.GarbageCollector.RunAsync().ConfigureAwait(false);
                listener.RecordObservableInstruments();
                string all;
                lock (seen) all = String.Join("\n", seen);
                AssertContains("armada.blackbox.entries:1:phase=Completion,step=ScopeViolation,kind=ScopeViolation,triage=Unknown", all, "entries counter");
                AssertContains("armada.blackbox.gc_deleted:1:reason=expired", all, "gc counter");
                AssertContains("armada.blackbox.bytes:", all, "bytes gauge");
                AssertTrue(BlackBoxMetrics.BytesSnapshot().ContainsKey("Database"), "bytes per target");
            }));

            cases.Add(Case("settings_change_in_place", "A settings update copies into the live object; ApplySettingsChanged drops cached stores and recomputes expiry when retention changed", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                string id = await CaptureAsync(bb, t).ConfigureAwait(false);
                BlackBoxEntry before = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(before.CreatedUtc.AddDays(180).ToString("u"), before.ExpiresUtc!.Value.ToUniversalTime().ToString("u"), "default expiry");
                BlackBoxSettings live = settings.BlackBox;
                BlackBoxRetentionSettings previous = live.Retention.Clone();
                BlackBoxSettings incoming = new BlackBoxSettings();
                incoming.Retention.RetentionDays = 7;
                incoming.Storage.Target = BlackBoxStorageTargetEnum.Disk;
                incoming.Storage.Disk.Directory = TestTemp.NewDirectory("bbinplace");
                live.CopyFrom(incoming);
                AssertTrue(ReferenceEquals(live, settings.BlackBox), "same instance");
                AssertEqual(7, settings.BlackBox.Retention.RetentionDays, "copied");
                IBlackBoxStore cached = bb.Stores.Get(BlackBoxStorageTargetEnum.Database);
                await bb.ApplySettingsChanged(previous).ConfigureAwait(false);
                AssertFalse(ReferenceEquals(cached, bb.Stores.Get(BlackBoxStorageTargetEnum.Database)), "stores rebuilt");
                BlackBoxEntry after = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertEqual(before.CreatedUtc.AddDays(7).ToString("u"), after.ExpiresUtc!.Value.ToUniversalTime().ToString("u"), "expiry recomputed");
                string onDisk = await CaptureAsync(bb, t).ConfigureAwait(false);
                AssertEqual(BlackBoxStorageTargetEnum.Disk, (await t.Driver.BlackBoxEntries.ReadAsync(onDisk).ConfigureAwait(false))!.StorageTarget, "new target used");
                Task unchanged = bb.ApplySettingsChanged(settings.BlackBox.Retention.Clone());
                AssertTrue(unchanged.IsCompleted, "no recompute when retention is unchanged");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box storage targets", cases: cases);
        }

        private static ArmadaSettings DeadS3()
        {
            ArmadaSettings settings = BlackBoxTestKit.Settings();
            settings.BlackBox.CaptureTimeoutMs = 30000;
            settings.BlackBox.Storage.Target = BlackBoxStorageTargetEnum.S3;
            settings.BlackBox.Storage.S3.Endpoint = "http://127.0.0.1:" + TestPorts.Reserve(1)[0];
            settings.BlackBox.Storage.S3.UseSsl = false;
            settings.BlackBox.Storage.S3.Bucket = "armada";
            settings.BlackBox.Storage.S3.AccessKey = "deadaccess";
            settings.BlackBox.Storage.S3.SecretKey = "dead-secret-value";
            return settings;
        }

        private static async Task<string> CaptureAsync(BlackBoxRuntime bb, TestDatabase t)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
            mission.AgentOutput = "agent output for the storage target suite";
            await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            string? id = await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false);
            AssertNotNull(id, "captured");
            return id!;
        }

        private static async Task<BlackBoxStorageMigrationJob> WaitAsync(BlackBoxRuntime bb, string id)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                BlackBoxStorageMigrationJob job = (await bb.Migrator.GetAsync(id).ConfigureAwait(false))!;
                if (job.State != BlackBoxJobStateEnum.Pending && job.State != BlackBoxJobStateEnum.Running) return job;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("migration " + id + " did not finish");
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        private static async Task<bool> ThrowsOrFalseAsync(Func<Task<bool>> check)
        {
            try { return await check().ConfigureAwait(false); }
            catch (BlackBoxStoreUnavailableException) { return false; }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
