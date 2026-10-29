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
    /// IBlackBoxService: tenant and owner scoping, reads (entry, record, section, digest, brief), objects and chunked reads,
    /// downloads, notes, resolve, pin, delete, purge, status and aggregates, and the wave-two placeholders.
    /// </summary>
    public sealed class BlackBoxServiceSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxService";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("scoping_and_reads", "Admins see every tenant, tenant admins their tenant, users their own entries; others read as not found; digest, section, brief and record reads", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                IBlackBoxService svc = bb.Service;
                BlackBoxEntry mine = await Seed(t.Driver, bb, "ten_a", "usr_a").ConfigureAwait(false);
                BlackBoxEntry colleague = await Seed(t.Driver, bb, "ten_a", "usr_b").ConfigureAwait(false);
                BlackBoxEntry foreign = await Seed(t.Driver, bb, "ten_z", "usr_z").ConfigureAwait(false);
                AuthContext user = BlackBoxTestKit.User("ten_a", "usr_a");
                AuthContext tenantAdmin = BlackBoxTestKit.User("ten_a", "usr_t", true);
                AssertEqual(mine.Id, (await svc.GetEntryAsync(user, mine.Id).ConfigureAwait(false)).Id, "own entry");
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => svc.GetEntryAsync(user, colleague.Id), "colleague's entry hidden from a user").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => svc.GetEntryAsync(tenantAdmin, foreign.Id), "other tenant hidden (not found, not forbidden)").ConfigureAwait(false);
                AssertEqual(colleague.Id, (await svc.GetEntryAsync(tenantAdmin, colleague.Id).ConfigureAwait(false)).Id, "tenant admin sees the tenant");
                AssertEqual(foreign.Id, (await svc.GetEntryAsync(BlackBoxTestKit.Admin(), foreign.Id).ConfigureAwait(false)).Id, "admin sees all");
                AssertEqual(1L, (await svc.EnumerateAsync(user, new BlackBoxEnumerateRequest()).ConfigureAwait(false)).TotalRecords, "user list");
                AssertEqual(2L, (await svc.EnumerateAsync(tenantAdmin, new BlackBoxEnumerateRequest { TenantId = "ten_z" }).ConfigureAwait(false)).TotalRecords, "tenant admin cannot widen to another tenant");
                AssertEqual(3L, (await svc.EnumerateAsync(BlackBoxTestKit.Admin(), null).ConfigureAwait(false)).TotalRecords, "admin list");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.EnumerateAsync(new AuthContext(), null), "unauthenticated").ConfigureAwait(false);

                BlackBoxEntry withRecord = await svc.GetMissionEntryAsync(user, mine.MissionId!).ConfigureAwait(false);
                AssertNotNull(withRecord.Record, "record included"); AssertEqual(mine.Id, withRecord.Record!.EntryId);
                AssertNull((await svc.GetEntryAsync(user, mine.Id, false).ConfigureAwait(false)).Record, "record left out when not asked");
                AssertEqual(mine.Id, await svc.ResolveEntryIdAsync(user, mine.MissionId, null, null).ConfigureAwait(false), "resolve by mission");
                await AssertThrowsAsync<ArgumentException>(() => svc.ResolveEntryIdAsync(user, null, null, null), "nothing to resolve").ConfigureAwait(false);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => svc.GetMissionEntryAsync(user, "msn_none"), "mission without entry").ConfigureAwait(false);
                BlackBoxDigest digest = await svc.GetDigestAsync(user, mine.Id).ConfigureAwait(false);
                AssertEqual(BlackBoxStepEnum.ScopeViolation, digest.Step); AssertNotNull(digest.SourceFile, "site"); AssertTrue(JsonSerializer.Serialize(digest).Length < 4096, "digest stays small");
                BlackBoxSectionResult section = await svc.GetSectionAsync(user, mine.Id, BlackBoxSectionEnum.Site).ConfigureAwait(false);
                AssertNotNull(section.Record.Site, "requested section"); AssertNull(section.Record.Subject, "other sections left out");
                await AssertThrowsAsync<ArgumentException>(() => svc.GetSectionAsync(user, mine.Id, BlackBoxSectionEnum.Unknown), "unknown section").ConfigureAwait(false);
                string brief = await svc.GetBriefAsync(user, mine.Id, BlackBoxAudienceEnum.User).ConfigureAwait(false);
                AssertStartsWith("# What went wrong", brief);
                EnumerationResult<BlackBoxEntrySummary> attempts = await svc.EnumerateMissionEntriesAsync(user, mine.MissionId!, null).ConfigureAwait(false);
                AssertEqual(1L, attempts.TotalRecords, "attempts");
            }));

            cases.Add(Case("objects_and_chunks", "Objects list without data; chunk reads page a large log with nextOffset and totalBytes; bounds are enforced; download zips every object plus manifest.json", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                IBlackBoxService svc = bb.Service;
                AuthContext admin = BlackBoxTestKit.Admin();
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                StringBuilder log = new StringBuilder();
                for (int i = 0; i < 20000; i++) log.Append("line ").Append(i).Append(" é中\n");
                mission.AgentOutput = log.ToString();
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                EnumerationResult<BlackBoxObjectSummary> objects = await svc.EnumerateObjectsAsync(admin, id, null).ConfigureAwait(false);
                AssertTrue(objects.Objects.Any(o => o.Name == BlackBoxObjectNames.AgentOutput), "agent output listed");
                long offset = 0; int chunks = 0; long total = 0; StringBuilder rebuilt = new StringBuilder();
                while (true)
                {
                    BlackBoxObjectChunk chunk = await svc.ReadObjectChunkAsync(admin, id, BlackBoxObjectNames.AgentOutput, offset).ConfigureAwait(false);
                    AssertTrue(chunk.Length <= BlackBoxLimits.MaxMcpChunkBytes, "chunk at most 64 KB");
                    AssertEqual("utf-8", chunk.Encoding);
                    AssertFalse(chunk.Content.Contains('�'), "chunks end on character boundaries");
                    rebuilt.Append(chunk.Content); total = chunk.TotalBytes; chunks++;
                    if (chunk.NextOffset == null) break;
                    offset = chunk.NextOffset.Value;
                }
                AssertTrue(chunks > 3, "paged in several chunks: " + chunks);
                AssertEqual(total, (long)Encoding.UTF8.GetByteCount(rebuilt.ToString()), "chunks add up to the object");
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => svc.ReadObjectChunkAsync(admin, id, BlackBoxObjectNames.AgentOutput, 0, BlackBoxLimits.MaxMcpChunkBytes + 1), "over 64 KB").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => svc.ReadObjectAsync(admin, id, BlackBoxObjectNames.AgentOutput, -1), "negative offset").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentOutOfRangeException>(() => svc.ReadObjectAsync(admin, id, BlackBoxObjectNames.AgentOutput, 0, BlackBoxLimits.MaxRestChunkBytes + 1), "over 1 MB").ConfigureAwait(false);
                BlackBoxObjectChunk past = await svc.ReadObjectChunkAsync(admin, id, BlackBoxObjectNames.Brief, 10000000).ConfigureAwait(false);
                AssertEqual(0L, past.Length, "past the end is empty"); AssertNull(past.NextOffset, "no next");

                byte[] zip = await svc.DownloadAsync(admin, id).ConfigureAwait(false);
                using (ZipArchive archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                {
                    AssertNotNull(archive.GetEntry(BlackBoxObjectNames.Manifest), "manifest.json");
                    AssertNotNull(archive.GetEntry(BlackBoxObjectNames.Record), "record.json");
                    using (StreamReader reader = new StreamReader(archive.GetEntry(BlackBoxObjectNames.Manifest)!.Open()))
                    {
                        BlackBoxDownloadManifest manifest = JsonSerializer.Deserialize<BlackBoxDownloadManifest>(reader.ReadToEnd(), BlackBoxJson.Options)!;
                        AssertEqual(id, manifest.EntryId); AssertEqual(objects.TotalRecords, (long)manifest.Objects.Count, "every object listed");
                        AssertTrue(manifest.Objects.All(o => o.Sha256 != null && o.Sha256.Length == 64), "hashes");
                    }
                }
            }));

            cases.Add(Case("notes_resolve_pin_delete_purge", "Notes, resolve, pin, delete one and many, and purge (dry run by default, pinned kept)", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, null, sink);
                IBlackBoxService svc = bb.Service;
                AuthContext user = BlackBoxTestKit.User("ten_a", "usr_a");
                BlackBoxEntry e = await Seed(t.Driver, bb, "ten_a", "usr_a").ConfigureAwait(false);
                BlackBoxNote note = await svc.AddNoteAsync(user, e.Id, new BlackBoxNoteAddRequest { Body = "  root cause found  " }, BlackBoxNoteAuthorKindEnum.Agent).ConfigureAwait(false);
                AssertEqual("root cause found", note.Body); AssertEqual(BlackBoxNoteAuthorKindEnum.Agent, note.AuthorKind); AssertEqual("usr_a", note.UserId);
                await AssertThrowsAsync<ArgumentException>(() => svc.AddNoteAsync(user, e.Id, new BlackBoxNoteAddRequest { Body = " " }), "empty note").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => svc.AddNoteAsync(user, e.Id, new BlackBoxNoteAddRequest { Body = new string('x', 8001) }), "long note").ConfigureAwait(false);
                AssertEqual(1L, (await svc.EnumerateNotesAsync(user, e.Id, null).ConfigureAwait(false)).TotalRecords, "notes");
                AssertContains("root cause found", await svc.GetBriefAsync(user, e.Id).ConfigureAwait(false), "brief carries notes");
                AssertEqual(1L, (await svc.SearchAsync(user, new BlackBoxSearchRequest { Query = "ROOT CAUSE" }).ConfigureAwait(false)).TotalRecords, "search finds notes");
                await AssertThrowsAsync<ArgumentException>(() => svc.SearchAsync(user, new BlackBoxSearchRequest { Query = new string('q', 201) }), "query too long").ConfigureAwait(false);

                BlackBoxEntry resolved = await svc.ResolveAsync(user, e.Id, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Resolved, Note = "fixed", Link = "msn_fix" }).ConfigureAwait(false);
                AssertEqual(BlackBoxResolutionEnum.Resolved, resolved.Resolution); AssertNotNull(resolved.ResolvedUtc, "resolved time");
                AssertTrue(resolved.ExpiresUtc < DateTime.UtcNow.AddDays(31), "resolution shortens expiry");
                AssertEqual(1, sink.Count(BlackBoxEventTypes.Resolved), "resolved event");
                await AssertThrowsAsync<ArgumentException>(() => svc.ResolveAsync(user, e.Id, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Unknown }), "unknown resolution").ConfigureAwait(false);
                BlackBoxEntry pinned = await svc.PinAsync(user, e.Id, true).ConfigureAwait(false);
                AssertTrue(pinned.Pinned, "pinned"); AssertNull(pinned.ExpiresUtc, "pinned never expires");
                AssertNotNull((await svc.PinAsync(user, e.Id, false).ConfigureAwait(false)).ExpiresUtc, "unpinned expires again");

                BlackBoxEntry p1 = await Seed(t.Driver, bb, "ten_a", "usr_a").ConfigureAwait(false);
                BlackBoxEntry p2 = await Seed(t.Driver, bb, "ten_a", "usr_a").ConfigureAwait(false);
                await svc.PinAsync(user, p2.Id, true).ConfigureAwait(false);
                BlackBoxPurgeResult dry = await svc.PurgeAsync(user, new BlackBoxPurgeRequest()).ConfigureAwait(false);
                AssertTrue(dry.DryRun, "dry run by default"); AssertEqual(2L, dry.Matched, "matched unpinned"); AssertEqual(1, dry.SkippedPinned); AssertEqual(0, dry.Deleted);
                AssertEqual(3L, (await svc.EnumerateAsync(user, null).ConfigureAwait(false)).TotalRecords, "dry run deleted nothing");
                BlackBoxPurgeResult real = await svc.PurgeAsync(user, new BlackBoxPurgeRequest { DryRun = false, Fingerprint = p1.Fingerprint }).ConfigureAwait(false);
                AssertEqual(2, real.Deleted, "purged the matching unpinned entries");
                AssertNotNull(await t.Driver.BlackBoxEntries.ReadAsync(p2.Id).ConfigureAwait(false), "pinned survives the purge");

                BlackBoxDeleteResult one = await svc.DeleteAsync(user, p2.Id).ConfigureAwait(false);
                AssertEqual(1, one.Deleted);
                await AssertThrowsAsync<BlackBoxNotFoundException>(() => svc.DeleteAsync(user, p2.Id), "already deleted").ConfigureAwait(false);
                BlackBoxDeleteManyRequest many = new BlackBoxDeleteManyRequest();
                many.Ids.AddRange(new[] { "bbx_missing", "" });
                BlackBoxDeleteResult none = await svc.DeleteManyAsync(user, many).ConfigureAwait(false);
                AssertEqual(0, none.Deleted); AssertEqual("bbx_missing", none.Skipped.Single());
                BlackBoxDeleteManyRequest tooMany = new BlackBoxDeleteManyRequest { Ids = Enumerable.Range(0, 1001).Select(i => "bbx_" + i).ToList() };
                await AssertThrowsAsync<CollectionLimitExceededException>(() => svc.DeleteManyAsync(user, tooMany), "1001 ids").ConfigureAwait(false);
            }));

            cases.Add(Case("status_aggregates_and_wave2", "Retention and storage status, fingerprints and hot spots are tenant-scoped; wave-two operations say they are not implemented", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                IBlackBoxService svc = bb.Service;
                AuthContext user = BlackBoxTestKit.User("ten_a", "usr_a");
                await Seed(t.Driver, bb, "ten_a", "usr_a").ConfigureAwait(false);
                await Seed(t.Driver, bb, "ten_z", "usr_z").ConfigureAwait(false);
                BlackBoxRetentionStatus retention = await svc.GetRetentionStatusAsync(user).ConfigureAwait(false);
                AssertEqual(1L, retention.TotalEntries, "tenant-scoped count"); AssertTrue(retention.TotalBytes > 0, "bytes");
                AssertEqual(180, retention.Policy.RetentionDays, "policy");
                BlackBoxStorageStatus storage = await svc.GetStorageStatusAsync(BlackBoxTestKit.Admin()).ConfigureAwait(false);
                AssertEqual(BlackBoxStorageTargetEnum.Database, storage.Target); AssertTrue(storage.Health.Healthy, "healthy"); AssertEqual(2L, storage.Usage.Single().Entries, "usage");
                AssertEqual(1L, (await svc.EnumerateFingerprintsAsync(user, null).ConfigureAwait(false)).Objects.Sum(f => f.Count), "fingerprints tenant-scoped");
                AssertEqual(0L, (await svc.EnumerateHotspotsAsync(user, null).ConfigureAwait(false)).TotalRecords, "no conflicted files yet");
                BlackBoxGcReport gc = await svc.RunGcAsync(BlackBoxTestKit.Admin(), new BlackBoxGcRunRequest { DryRun = true }).ConfigureAwait(false);
                AssertTrue(gc.DryRun, "gc dry run");
                BlackBoxBundleResult bundle = await svc.GetBundleAsync(user, new BlackBoxBundleRequest()).ConfigureAwait(false);
                AssertEqual(1, bundle.EntryCount); AssertContains("maintainer bundle", bundle.Content);
                await AssertThrowsAsync<NotImplementedException>(() => svc.RunRecoveryAsync(user, "bbx_x", "rp1", new BlackBoxRecoveryRunRequest()), "recovery").ConfigureAwait(false);
                await AssertThrowsAsync<NotImplementedException>(() => svc.ApplyRecommendationAsync(user, "bbx_x", "cr1", new BlackBoxRecommendationApplyRequest()), "recommendation").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.StartStorageMigrationAsync(user, new BlackBoxStorageMigrationRequest()), "migration needs a global admin").ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => svc.GetStorageMigrationAsync(user, "bbm_x"), "migration status needs a global admin").ConfigureAwait(false);
                BlackBoxSettings masked = BlackBoxSettingsSecrets.Mask(new BlackBoxSettings { Storage = new BlackBoxStorageSettings { S3 = new BlackBoxS3Settings { SecretKey = "realsecret", AccessKey = "AKIAREAL" } } }, "********");
                AssertEqual("********", masked.Storage.S3.SecretKey); AssertNull(masked.Storage.Cifs.Password, "unset stays null");
                BlackBoxSettings current = new BlackBoxSettings(); current.Storage.S3.SecretKey = "realsecret";
                BlackBoxSettings incoming = new BlackBoxSettings(); incoming.Storage.S3.SecretKey = "********";
                BlackBoxSettingsSecrets.KeepMasked(incoming, current, "********");
                AssertEqual("realsecret", incoming.Storage.S3.SecretKey, "masked write keeps the stored secret");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box service", cases: cases);
        }

        private static async Task<BlackBoxEntry> Seed(DatabaseDriver db, BlackBoxRuntime bb, string tenantId, string userId)
        {
            Mission mission = await BlackBoxTestKit.FailedMissionAsync(db).ConfigureAwait(false);
            mission.TenantId = tenantId;
            mission.UserId = userId;
            string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
            return (await db.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
