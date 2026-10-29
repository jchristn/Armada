namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server for the mission and voyage black box: every client method in
    /// ArmadaClient.BlackBox.cs (reads, briefs, sections, objects in ranges, download, lists, search, notes, resolve, pin,
    /// delete, purge, garbage collection, retention, storage, backfill, bundle, hotspots and fingerprints), and the typed
    /// error codes the client carries: 404 BlackBoxNotFound, 400 InvalidArgument, 409 BlackBoxDeleting, 410
    /// BlackBoxObjectMissing, 501 NotImplemented, and 503 BlackBoxStoreUnavailable (from a stub, since a healthy live
    /// store never answers it).
    /// </summary>
    public sealed class ClientContractBlackBoxSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.BlackBox";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("reads", "Mission, voyage and entry reads, briefs by audience, sections, objects in ranges and the download", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                BlackBoxEntry mission = (await c.GetMissionBlackBoxAsync(seeded.MissionId))!;
                AssertEqual(seeded.EntryId, mission.Id, "mission entry");
                AssertNotNull(mission.Record, "record");
                AssertNull((await c.GetMissionBlackBoxAsync(seeded.MissionId, false))!.Record, "includeRecord false sent");
                AssertEqual(seeded.VoyageEntryId, (await c.GetVoyageBlackBoxAsync(seeded.VoyageId))!.Id, "voyage entry");
                AssertEqual(seeded.EntryId, (await c.GetBlackBoxAsync(seeded.EntryId))!.Id, "entry");

                string agent = (await c.GetBlackBoxBriefAsync(seeded.EntryId))!;
                string user = (await c.GetBlackBoxBriefAsync(seeded.EntryId, BlackBoxAudienceEnum.User))!;
                string maintainer = (await c.GetBlackBoxBriefAsync(seeded.EntryId, BlackBoxAudienceEnum.Maintainer, true))!;
                AssertTrue(agent.Length > 0 && user.Length > 0 && maintainer.Length > 0, "briefs");
                AssertNotEqual(agent, user, "audience sent");
                AssertEqual(agent, (await c.GetMissionBlackBoxBriefAsync(seeded.MissionId))!, "mission brief");
                AssertTrue((await c.GetVoyageBlackBoxBriefAsync(seeded.VoyageId, BlackBoxAudienceEnum.User))!.Length > 0, "voyage brief");

                BlackBoxSectionResult section = (await c.GetBlackBoxSectionAsync(seeded.EntryId, BlackBoxSectionEnum.Status))!;
                AssertNotNull(section.Record.Status, "section");

                EnumerationResult<BlackBoxEntrySummary> attempts = (await c.EnumerateMissionBlackBoxEntriesAsync(seeded.MissionId))!;
                AssertEqual(1L, attempts.TotalRecords, "attempts");
                EnumerationResult<BlackBoxEntrySummary> attemptsGet = (await c.ListMissionBlackBoxEntriesAsync(seeded.MissionId, 1, 3))!;
                AssertEqual(3, attemptsGet.PageSize, "GET twin page size sent");
                AssertEqual(seeded.EntryId, attemptsGet.Objects.Single().Id, "GET twin");
                EnumerationResult<BlackBoxObjectSummary> objectsGet = (await c.ListBlackBoxObjectsAsync(seeded.EntryId, 1, 2))!;
                AssertEqual(2, objectsGet.Objects.Count, "objects GET twin");
                EnumerationResult<BlackBoxObjectSummary> objects = (await c.EnumerateBlackBoxObjectsAsync(seeded.EntryId, new EnumerationQuery { PageSize = 100 }))!;
                AssertTrue(objects.Objects.Any(o => o.Name == "evidence/contract.log"), "objects");

                BlackBoxObjectContent first = await c.ReadBlackBoxObjectAsync(seeded.EntryId, "evidence/contract.log", 0, 1000);
                AssertEqual(1000, first.Content.Length, "range length sent");
                AssertEqual(2500L, first.TotalBytes, "total bytes header");
                AssertEqual(1000L, first.NextOffset, "next offset header");
                AssertEqual("text/plain", first.ContentType, "content type");
                AssertNotNull(first.ETag, "ETag");
                BlackBoxObjectContent rest = await c.ReadBlackBoxObjectAsync(seeded.EntryId, "evidence/contract.log", first.NextOffset);
                AssertEqual(1500, rest.Content.Length, "rest");
                AssertNull(rest.NextOffset, "end reached");
                AssertTrue(first.Content.Concat(rest.Content).SequenceEqual(seeded.Log), "bytes");

                BlackBoxDownload download = await c.DownloadBlackBoxAsync(seeded.EntryId);
                AssertEqual(seeded.EntryId + ".zip", download.FileName, "file name");
                using (ZipArchive zip = new ZipArchive(new MemoryStream(download.Content), ZipArchiveMode.Read))
                    AssertNotNull(zip.GetEntry(BlackBoxObjectNames.Manifest), "manifest.json");
            }));

            cases.Add(Case("lists_and_writes", "Enumerate, search, bundle, hotspots, fingerprints, notes, resolve, pin, unpin and reanalyze", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                EnumerationResult<BlackBoxEntrySummary> page = (await c.EnumerateBlackBoxAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, PageSize = 5 }))!;
                AssertEqual(seeded.EntryId, page.Objects.Single().Id, "enumerate filter sent");
                AssertEqual(5, page.PageSize, "page size sent");
                AssertNotNull(await c.EnumerateBlackBoxAsync(), "no query");
                EnumerationResult<BlackBoxEntrySummary> listed = (await c.ListBlackBoxAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, Phase = BlackBoxPhaseEnum.Completion, IsCurrent = true }))!;
                AssertEqual(seeded.EntryId, listed.Objects.Single().Id, "GET list with query filters");
                EnumerationResult<BlackBoxEntrySummary> wrongPhase = (await c.ListBlackBoxAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, Phase = BlackBoxPhaseEnum.Landing }))!;
                AssertEqual(0L, wrongPhase.TotalRecords, "enum filter sent in the query");
                EnumerationResult<BlackBoxEntrySummary> found = (await c.SearchBlackBoxAsync(new BlackBoxSearchRequest { Query = seeded.Marker }))!;
                AssertTrue(found.Objects.Any(o => o.Id == seeded.EntryId), "search");
                BlackBoxBundleResult bundle = (await c.GetBlackBoxBundleAsync(new BlackBoxBundleRequest { MissionId = seeded.MissionId, Max = 1 }))!;
                AssertEqual(1, bundle.EntryCount, "bundle");
                EnumerationResult<BlackBoxHotspot> hotspots = (await c.EnumerateBlackBoxHotspotsAsync(new BlackBoxHotspotEnumerateRequest { Role = BlackBoxFileRoleEnum.OutOfScope, PageSize = 50 }))!;
                AssertTrue(hotspots.Objects.Any(h => h.Directory == "lib/contract"), "hotspot role sent");
                AssertNotNull(await c.EnumerateBlackBoxFingerprintsAsync(new BlackBoxFingerprintEnumerateRequest { PageSize = 5 }), "fingerprints");
                BlackBoxEntry fingerprinted = (await c.GetBlackBoxAsync(seeded.EntryId, false))!;
                BlackBoxFingerprintTrend trend = (await c.GetBlackBoxFingerprintTrendAsync(fingerprinted.Fingerprint!))!;
                AssertTrue(trend.Total >= 1, "fingerprint trend");
                BlackBoxFixture fixture = (await c.ExportBlackBoxFixtureAsync(seeded.EntryId, "contract-fixture"))!;
                AssertEqual("contract-fixture", fixture.Name, "fixture name sent");

                BlackBoxNote note = (await c.AddBlackBoxNoteAsync(seeded.EntryId, new BlackBoxNoteAddRequest { Body = "contract note" }))!;
                AssertEqual("contract note", note.Body, "note");
                EnumerationResult<BlackBoxNote> notes = (await c.EnumerateBlackBoxNotesAsync(seeded.EntryId))!;
                AssertTrue(notes.Objects.Any(n => n.Id == note.Id), "notes");
                EnumerationResult<BlackBoxNote> notesGet = (await c.ListBlackBoxNotesAsync(seeded.EntryId, 1, 5))!;
                AssertTrue(notesGet.Objects.Any(n => n.Id == note.Id), "notes GET twin");
                AssertEqual(BlackBoxResolutionEnum.Duplicate, (await c.ResolveBlackBoxAsync(seeded.EntryId, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Duplicate, Link = "bbx_other" }))!.Resolution, "resolve");
                AssertTrue((await c.PinBlackBoxAsync(seeded.EntryId))!.Pinned, "pin");
                AssertFalse((await c.UnpinBlackBoxAsync(seeded.EntryId))!.Pinned, "unpin");
                AssertEqual(seeded.EntryId, (await c.ReanalyzeBlackBoxAsync(seeded.EntryId))!.Id, "reanalyze");
            }));

            cases.Add(Case("admin_and_delete", "Retention, storage, garbage collection and purge dry runs, backfill, delete one and many", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                AssertTrue((await c.GetBlackBoxRetentionAsync())!.TotalEntries >= 1, "retention");
                AssertEqual(BlackBoxStorageTargetEnum.Database, (await c.GetBlackBoxStorageAsync())!.Target, "storage");
                AssertTrue((await c.RunBlackBoxGcAsync(true))!.DryRun, "gc dry run sent");
                BlackBoxPurgeResult purge = (await c.PurgeBlackBoxAsync(new BlackBoxPurgeRequest { MissionId = seeded.MissionId }))!;
                AssertTrue(purge.DryRun && purge.Matched == 1 && purge.Deleted == 0, "purge dry run");
                AssertEqual(BlackBoxCaptureModeEnum.Backfill, (await c.BackfillBlackBoxAsync(new BlackBoxBackfillRequest { MaxSubjects = 1 }))!.Mode, "backfill");

                BlackBoxDeleteResult many = (await c.DeleteBlackBoxEntriesAsync(new List<string> { seeded.VoyageEntryId, "bbx_missing_" + ClientContract.Suffix() }))!;
                AssertEqual(1, many.Deleted, "delete many");
                AssertEqual(1, many.Skipped.Count, "missing id skipped");
                AssertEqual(1, (await c.DeleteBlackBoxAsync(seeded.EntryId))!.Deleted, "delete one");
                ArmadaApiException gone = await ClientContract.ExpectErrorAsync(() => c.GetBlackBoxAsync(seeded.EntryId), "deleted entry", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, gone.Code, "BlackBoxNotFound code");
                ArmadaApiException limit = await ClientContract.ExpectErrorAsync(() => c.DeleteBlackBoxEntriesAsync(Enumerable.Range(0, 1001).Select(i => "bbx_" + i).ToList()), "1001 ids", 400, 400);
                AssertEqual("CollectionLimitExceeded", limit.Code, "limit code");
            }));

            cases.Add(Case("typed_errors", "404, 400, 409, 410 and 501 replies carry the black box code (recovery: unknown path 404, confirmed without a token 409)", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                ArmadaApiException notFound = await ClientContract.ExpectErrorAsync(() => c.GetMissionBlackBoxAsync("msn_missing_" + ClientContract.Suffix()), "unknown mission", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, notFound.Code, "404 code");
                ArmadaApiException badLength = await ClientContract.ExpectErrorAsync(() => c.ReadBlackBoxObjectAsync(seeded.EntryId, "evidence/contract.log", 0, 1048577), "length over 1 MB", 400, 400);
                AssertEqual("InvalidArgument", badLength.Code, "400 code");
                ArmadaApiException missing = await ClientContract.ExpectErrorAsync(() => c.ReadBlackBoxObjectAsync(seeded.EntryId, "evidence/missing.log"), "missing object", 410, 410);
                AssertEqual(BlackBoxObjectMissingException.ErrorCode, missing.Code, "410 code");

                ArmadaApiException unknownPath = await ClientContract.ExpectErrorAsync(() => c.RunBlackBoxRecoveryAsync(seeded.EntryId, "no-such-path"), "unknown recovery path", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, unknownPath.Code, "404 code");
                ArmadaApiException unknownRecommendation = await ClientContract.ExpectErrorAsync(() => c.ApplyBlackBoxRecommendationAsync(seeded.EntryId, "no-such-recommendation", new BlackBoxRecommendationApplyRequest { DryRun = true }), "unknown recommendation", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, unknownRecommendation.Code, "404 code");
                BlackBoxRecoveryPath? path = (await c.GetBlackBoxAsync(seeded.EntryId))!.Record?.Analysis?.RecoveryPaths.FirstOrDefault(p => !p.DataLossRisk && p.Steps.Any(st => st.Action != null));
                AssertNotNull(path, "a runnable recovery path");
                BlackBoxRecoveryRunResult dry = (await c.RunBlackBoxRecoveryAsync(seeded.EntryId, path!.Id))!;
                AssertTrue(dry.DryRun && !String.IsNullOrEmpty(dry.ConfirmToken), "dry run by default with a token");
                ArmadaApiException noToken = await ClientContract.ExpectErrorAsync(() => c.RunBlackBoxRecoveryAsync(seeded.EntryId, path.Id, new BlackBoxRecoveryRunRequest { DryRun = false, AuthorKind = BlackBoxNoteAuthorKindEnum.Operator }), "confirmed without a token", 409, 409);
                AssertEqual(BlackBoxConfirmationException.ErrorCode, noToken.Code, "409 code");
                // TODO(blackbox-wave2: storage): flip to the job assertions when bb-storage merges storage migration.
                await ClientContract.ExpectErrorAsync(() => c.MigrateBlackBoxStorageAsync(new BlackBoxStorageMigrationRequest { To = BlackBoxStorageTargetEnum.Disk }), "migrate", 501, 501);
                await ClientContract.ExpectErrorAsync(() => c.GetBlackBoxStorageMigrationAsync("bbm_missing"), "migration job", 501, 501);

                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                    AssertTrue(await db.BlackBoxEntries.UpdateStateAsync(seeded.VoyageEntryId, BlackBoxStateEnum.Deleting).ConfigureAwait(false), "Deleting");
                // The voyage's current-entry lookup skips an entry being deleted (404); the entry read by id is 409.
                await ClientContract.ExpectErrorAsync(() => c.GetVoyageBlackBoxAsync(seeded.VoyageId), "Deleting entry by voyage", 404, 404);
                ArmadaApiException deletingById = await ClientContract.ExpectErrorAsync(() => c.GetBlackBoxAsync(seeded.VoyageEntryId), "Deleting entry by id", 409, 409);
                AssertEqual(BlackBoxDeletingException.ErrorCode, deletingById.Code, "409 code");
            }));

            cases.Add(new TestCaseDescriptor(
                suiteId: Suite,
                caseId: "store_unavailable_code",
                displayName: "A 503 BlackBoxStoreUnavailable reply is a typed exception with its code",
                executeAsync: async (CancellationToken ct) =>
                {
                    StubHttpHandler stub = new StubHttpHandler();
                    stub.Json("GET", "/api/v1/blackbox/bbx_stub", "{\"Error\":\"ServiceUnavailable\",\"StatusCode\":503,\"Message\":\"store down\",\"Data\":{\"Code\":\"BlackBoxStoreUnavailable\"}}", HttpStatusCode.ServiceUnavailable);
                    using (ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub))
                    {
                        ArmadaApiException ex = await ClientContract.ExpectErrorAsync(() => client.GetBlackBoxAsync("bbx_stub"), "store down", 503, 503).ConfigureAwait(false);
                        AssertEqual(BlackBoxStoreUnavailableException.ErrorCode, ex.Code, "503 code");
                    }
                },
                tags: new List<string> { TestTags.Negative }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: black box", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string caseId, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, caseId, name, body);
        }

        private static async Task<BlackBoxSeed> SeedAsync(E2EServerFixture fx)
        {
            BlackBoxRuntime runtime = fx.Server.BlackBox ?? throw new InvalidOperationException("no black box runtime");
            BlackBoxSeed seeded = new BlackBoxSeed();
            seeded.Marker = "contract-marker-" + ClientContract.Suffix();
            Mission mission;
            Voyage voyage;
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                mission = new Mission("Contract black box " + ClientContract.Suffix(), "Fix it");
                mission.Status = MissionStatusEnum.Failed;
                mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
                mission.FailureReason = "Out of scope " + seeded.Marker;
                mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                voyage = new Voyage("Contract voyage " + ClientContract.Suffix(), "voyage");
                voyage.Status = VoyageStatusEnum.Failed;
                voyage = await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
            }

            BlackBoxCaptureContext context = new BlackBoxCaptureContext { Mission = mission, StatusBefore = MissionStatusEnum.InProgress, Step = BlackBoxStepEnum.ScopeViolation };
            context.OutOfScopeFiles.Add("lib/contract/x.cs");
            seeded.EntryId = (await runtime.Capture.CaptureMissionFailureAsync(context).ConfigureAwait(false))!;
            seeded.VoyageEntryId = (await runtime.Capture.CaptureVoyageFailureAsync(new BlackBoxVoyageCaptureContext { Voyage = voyage, StatusBefore = VoyageStatusEnum.InProgress, Reason = "settlement" }).ConfigureAwait(false))!;
            await runtime.Queue.DrainAsync().ConfigureAwait(false);
            seeded.MissionId = mission.Id;
            seeded.VoyageId = voyage.Id;
            seeded.Log = Encoding.ASCII.GetBytes(new string('x', 2500));
            using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
            {
                BlackBoxEntry entry = (await db.BlackBoxEntries.ReadAsync(seeded.EntryId).ConfigureAwait(false))!;
                await runtime.Objects.WriteAsync(entry, "evidence/contract.log", "text/plain", seeded.Log, false, true).ConfigureAwait(false);
                BlackBoxObject missing = await runtime.Objects.WriteAsync(entry, "evidence/missing.log", "text/plain", Encoding.ASCII.GetBytes("gone"), false, true).ConfigureAwait(false);
                await db.BlackBoxObjects.SetMissingAsync(missing.Id, true).ConfigureAwait(false);
            }

            return seeded;
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(E2EServerFixture fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = Path.Combine(fx.TempDir, "armada.db") };
            DatabaseDriver db = DatabaseDriverFactory.Create(settings, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        #endregion
    }
}
