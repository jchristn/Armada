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
    /// BlackBoxObjectMissing, 404 for an unknown storage migration job, and 503 BlackBoxStoreUnavailable (from a stub,
    /// since a healthy live store never answers it).
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

            cases.Add(Case("reads", "Mission, voyage and entry reads, the brief, sections, objects in ranges and the download", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                BlackBoxEntry mission = (await c.BlackBox.GetForMissionAsync(seeded.MissionId))!;
                AssertEqual(seeded.EntryId, mission.Id, "mission entry");
                AssertNotNull(mission.Record, "record");
                AssertNull((await c.BlackBox.GetForMissionAsync(seeded.MissionId, false))!.Record, "includeRecord false sent");
                AssertEqual(seeded.VoyageEntryId, (await c.BlackBox.GetForVoyageAsync(seeded.VoyageId))!.Id, "voyage entry");
                AssertEqual(seeded.EntryId, (await c.BlackBox.GetAsync(seeded.EntryId))!.Id, "entry");

                string brief = (await c.BlackBox.GetBriefAsync(seeded.EntryId))!;
                string redacted = (await c.BlackBox.GetBriefAsync(seeded.EntryId, true))!;
                AssertTrue(brief.StartsWith("# FIX THIS", StringComparison.Ordinal) && redacted.StartsWith("# FIX THIS", StringComparison.Ordinal), "briefs");
                AssertEqual(brief, (await c.BlackBox.GetMissionBriefAsync(seeded.MissionId))!, "mission brief");
                AssertTrue((await c.BlackBox.GetVoyageBriefAsync(seeded.VoyageId, true))!.Length > 0, "voyage brief");

                BlackBoxSectionResult section = (await c.BlackBox.GetSectionAsync(seeded.EntryId, BlackBoxSectionEnum.Status))!;
                AssertNotNull(section.Record.Status, "section");

                EnumerationResult<BlackBoxEntrySummary> attempts = (await c.BlackBox.EnumerateMissionEntriesAsync(seeded.MissionId))!;
                AssertEqual(1L, attempts.TotalRecords, "attempts");
                EnumerationResult<BlackBoxEntrySummary> attemptsGet = (await c.BlackBox.ListMissionEntriesAsync(seeded.MissionId, 1, 3))!;
                AssertEqual(3, attemptsGet.PageSize, "GET twin page size sent");
                AssertEqual(seeded.EntryId, attemptsGet.Objects.Single().Id, "GET twin");
                EnumerationResult<BlackBoxObjectSummary> objectsGet = (await c.BlackBox.ListObjectsAsync(seeded.EntryId, 1, 2))!;
                AssertEqual(2, objectsGet.Objects.Count, "objects GET twin");
                EnumerationResult<BlackBoxObjectSummary> objects = (await c.BlackBox.EnumerateObjectsAsync(seeded.EntryId, new EnumerationQuery { PageSize = 100 }))!;
                AssertTrue(objects.Objects.Any(o => o.Name == "evidence/contract.log"), "objects");

                BlackBoxObjectContent first = await c.BlackBox.ReadObjectAsync(seeded.EntryId, "evidence/contract.log", 0, 1000);
                AssertEqual(1000, first.Content.Length, "range length sent");
                AssertEqual(2500L, first.TotalBytes, "total bytes header");
                AssertEqual(1000L, first.NextOffset, "next offset header");
                AssertEqual("text/plain", first.ContentType, "content type");
                AssertNotNull(first.ETag, "ETag");
                AssertEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first.Content)).ToLowerInvariant(), first.ETag, "ETag is the SHA-256 of the range returned");
                AssertNotNull(first.StoredSha256, "stored SHA-256 header");
                AssertNotEqual(first.ETag, (await c.BlackBox.ReadObjectAsync(seeded.EntryId, "evidence/contract.log", first.NextOffset)).ETag, "each range has its own ETag");
                BlackBoxObjectContent rest = await c.BlackBox.ReadObjectAsync(seeded.EntryId, "evidence/contract.log", first.NextOffset);
                AssertEqual(1500, rest.Content.Length, "rest");
                AssertNull(rest.NextOffset, "end reached");
                AssertTrue(first.Content.Concat(rest.Content).SequenceEqual(seeded.Log), "bytes");

                BlackBoxDownload download = await c.BlackBox.DownloadAsync(seeded.EntryId);
                AssertEqual(seeded.EntryId + ".zip", download.FileName, "file name");
                using (ZipArchive zip = new ZipArchive(new MemoryStream(download.Content), ZipArchiveMode.Read))
                    AssertNotNull(zip.GetEntry(BlackBoxObjectNames.Manifest), "manifest.json");
            }));

            cases.Add(Case("lists_and_writes", "Enumerate, search, bundle, hotspots, fingerprints, notes, resolve, pin, unpin and reanalyze", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                EnumerationResult<BlackBoxEntrySummary> page = (await c.BlackBox.EnumerateAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, PageSize = 5 }))!;
                AssertEqual(seeded.EntryId, page.Objects.Single().Id, "enumerate filter sent");
                AssertEqual(5, page.PageSize, "page size sent");
                AssertNotNull(await c.BlackBox.EnumerateAsync(), "no query");
                EnumerationResult<BlackBoxEntrySummary> listed = (await c.BlackBox.ListAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, Phase = BlackBoxPhaseEnum.Completion, IsCurrent = true }))!;
                AssertEqual(seeded.EntryId, listed.Objects.Single().Id, "GET list with query filters");
                EnumerationResult<BlackBoxEntrySummary> wrongPhase = (await c.BlackBox.ListAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, Phase = BlackBoxPhaseEnum.Landing }))!;
                AssertEqual(0L, wrongPhase.TotalRecords, "enum filter sent in the query");
                EnumerationResult<BlackBoxEntrySummary> found = (await c.BlackBox.SearchAsync(new BlackBoxSearchRequest { Query = seeded.Marker }))!;
                AssertTrue(found.Objects.Any(o => o.Id == seeded.EntryId), "search");
                BlackBoxBundleResult bundle = (await c.BlackBox.GetBundleAsync(new BlackBoxBundleRequest { MissionId = seeded.MissionId, Max = 1, AllFailures = true }))!;
                AssertEqual(1, bundle.EntryCount, "bundle");
                EnumerationResult<BlackBoxHotspot> hotspots = (await c.BlackBox.EnumerateHotspotsAsync(new BlackBoxHotspotEnumerateRequest { Role = BlackBoxFileRoleEnum.OutOfScope, PageSize = 50 }))!;
                AssertTrue(hotspots.Objects.Any(h => h.Directory == "lib/contract"), "hotspot role sent");
                AssertNotNull(await c.BlackBox.EnumerateFingerprintsAsync(new BlackBoxFingerprintEnumerateRequest { PageSize = 5 }), "fingerprints");
                EnumerationResult<BlackBoxResilienceGap> gaps = (await c.BlackBox.EnumerateResilienceGapsAsync(new BlackBoxResilienceGapRequest { PageSize = 500 }))!;
                AssertTrue(gaps.PageSize <= BlackBoxLimits.MaxResilienceGapPageSize, "resilience gaps page size capped and sent");
                AssertFalse(gaps.Objects.Any(g => g.LatestEntryId == seeded.EntryId), "the seeded completion failure is not a resilience gap");
                AssertNotNull(await c.BlackBox.EnumerateResilienceGapsAsync(), "resilience gaps without a request");
                EnumerationResult<BlackBoxEntrySummary> outside = (await c.BlackBox.ListAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, ResilienceScope = false }))!;
                AssertEqual(seeded.EntryId, outside.Objects.Single().Id, "resilienceScope=false sent in the query");
                EnumerationResult<BlackBoxEntrySummary> inside = (await c.BlackBox.EnumerateAsync(new BlackBoxEnumerateRequest { MissionId = seeded.MissionId, ResilienceScope = true }))!;
                AssertEqual(0L, inside.TotalRecords, "resilienceScope=true sent in the body");
                BlackBoxEntry fingerprinted = (await c.BlackBox.GetAsync(seeded.EntryId, false))!;
                BlackBoxFingerprintTrend trend = (await c.BlackBox.GetFingerprintTrendAsync(fingerprinted.Fingerprint!))!;
                AssertTrue(trend.Total >= 1, "fingerprint trend");
                BlackBoxFixture fixture = (await c.BlackBox.ExportFixtureAsync(seeded.EntryId, "contract-fixture"))!;
                AssertEqual("contract-fixture", fixture.Name, "fixture name sent");

                BlackBoxNote note = (await c.BlackBox.AddNoteAsync(seeded.EntryId, new BlackBoxNoteAddRequest { Body = "contract note" }))!;
                AssertEqual("contract note", note.Body, "note");
                EnumerationResult<BlackBoxNote> notes = (await c.BlackBox.EnumerateNotesAsync(seeded.EntryId))!;
                AssertTrue(notes.Objects.Any(n => n.Id == note.Id), "notes");
                EnumerationResult<BlackBoxNote> notesGet = (await c.BlackBox.ListNotesAsync(seeded.EntryId, 1, 5))!;
                AssertTrue(notesGet.Objects.Any(n => n.Id == note.Id), "notes GET twin");
                AssertEqual(BlackBoxResolutionEnum.Duplicate, (await c.BlackBox.ResolveAsync(seeded.EntryId, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Duplicate, Link = "bbx_other" }))!.Resolution, "resolve");
                AssertTrue((await c.BlackBox.PinAsync(seeded.EntryId))!.Pinned, "pin");
                AssertFalse((await c.BlackBox.UnpinAsync(seeded.EntryId))!.Pinned, "unpin");
                AssertEqual(seeded.EntryId, (await c.BlackBox.ReanalyzeAsync(seeded.EntryId))!.Id, "reanalyze");
            }));

            cases.Add(Case("admin_and_delete", "Retention, storage, garbage collection and purge dry runs, backfill, delete one and many", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                AssertTrue((await c.BlackBox.GetRetentionAsync())!.TotalEntries >= 1, "retention");
                AssertEqual(BlackBoxStorageTargetEnum.Database, (await c.BlackBox.GetStorageAsync())!.Target, "storage");
                AssertTrue((await c.BlackBox.RunGcAsync(true))!.DryRun, "gc dry run sent");
                BlackBoxPurgeResult purge = (await c.BlackBox.PurgeAsync(new BlackBoxPurgeRequest { MissionId = seeded.MissionId }))!;
                AssertTrue(purge.DryRun && purge.Matched == 1 && purge.Deleted == 0, "purge dry run");
                AssertEqual(BlackBoxCaptureModeEnum.Backfill, (await c.BlackBox.BackfillAsync(new BlackBoxBackfillRequest { MaxSubjects = 1 }))!.Mode, "backfill");

                BlackBoxDeleteResult many = (await c.BlackBox.DeleteEntriesAsync(new List<string> { seeded.VoyageEntryId, "bbx_missing_" + ClientContract.Suffix() }))!;
                AssertEqual(1, many.Deleted, "delete many");
                AssertEqual(1, many.Skipped.Count, "missing id skipped");
                AssertEqual(1, (await c.BlackBox.DeleteAsync(seeded.EntryId))!.Deleted, "delete one");
                ArmadaApiException gone = await ClientContract.ExpectErrorAsync(() => c.BlackBox.GetAsync(seeded.EntryId), "deleted entry", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, gone.Code, "BlackBoxNotFound code");
                ArmadaApiException limit = await ClientContract.ExpectErrorAsync(() => c.BlackBox.DeleteEntriesAsync(Enumerable.Range(0, 1001).Select(i => "bbx_" + i).ToList()), "1001 ids", 400, 400);
                AssertEqual("CollectionLimitExceeded", limit.Code, "limit code");
            }));

            cases.Add(Case("typed_errors", "404, 400, 409 and 410 replies carry the black box code (recovery: unknown path 404, confirmed without a token 409; storage migration: same source and target 400, unknown job 404)", async (c, fx) =>
            {
                BlackBoxSeed seeded = await SeedAsync(fx).ConfigureAwait(false);
                ArmadaApiException notFound = await ClientContract.ExpectErrorAsync(() => c.BlackBox.GetForMissionAsync("msn_missing_" + ClientContract.Suffix()), "unknown mission", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, notFound.Code, "404 code");
                ArmadaApiException badLength = await ClientContract.ExpectErrorAsync(() => c.BlackBox.ReadObjectAsync(seeded.EntryId, "evidence/contract.log", 0, 1048577), "length over 1 MB", 400, 400);
                AssertEqual("InvalidArgument", badLength.Code, "400 code");
                ArmadaApiException missing = await ClientContract.ExpectErrorAsync(() => c.BlackBox.ReadObjectAsync(seeded.EntryId, "evidence/missing.log"), "missing object", 410, 410);
                AssertEqual(BlackBoxObjectMissingException.ErrorCode, missing.Code, "410 code");

                ArmadaApiException unknownPath = await ClientContract.ExpectErrorAsync(() => c.BlackBox.RunRecoveryAsync(seeded.EntryId, "no-such-path"), "unknown recovery path", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, unknownPath.Code, "404 code");
                ArmadaApiException unknownRecommendation = await ClientContract.ExpectErrorAsync(() => c.BlackBox.ApplyRecommendationAsync(seeded.EntryId, "no-such-recommendation", new BlackBoxRecommendationApplyRequest { DryRun = true }), "unknown recommendation", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, unknownRecommendation.Code, "404 code");
                BlackBoxRecoveryPath? path = (await c.BlackBox.GetAsync(seeded.EntryId))!.Record?.Analysis?.RecoveryPaths.FirstOrDefault(p => !p.DataLossRisk && p.Steps.Any(st => st.Action != null));
                AssertNotNull(path, "a runnable recovery path");
                BlackBoxRecoveryRunResult dry = (await c.BlackBox.RunRecoveryAsync(seeded.EntryId, path!.Id))!;
                AssertTrue(dry.DryRun && !String.IsNullOrEmpty(dry.ConfirmToken), "dry run by default with a token");
                ArmadaApiException noToken = await ClientContract.ExpectErrorAsync(() => c.BlackBox.RunRecoveryAsync(seeded.EntryId, path.Id, new BlackBoxRecoveryRunRequest { DryRun = false, AuthorKind = BlackBoxNoteAuthorKindEnum.Operator }), "confirmed without a token", 409, 409);
                AssertEqual(BlackBoxConfirmationException.ErrorCode, noToken.Code, "409 code");
                ArmadaApiException sameTarget = await ClientContract.ExpectErrorAsync(() => c.BlackBox.MigrateStorageAsync(new BlackBoxStorageMigrationRequest { From = BlackBoxStorageTargetEnum.Database, To = BlackBoxStorageTargetEnum.Database }), "migrate to the same target", 400, 400);
                AssertEqual("InvalidArgument", sameTarget.Code, "400 code");
                ArmadaApiException noJob = await ClientContract.ExpectErrorAsync(() => c.BlackBox.GetStorageMigrationAsync("bbm_missing"), "unknown migration job", 404, 404);
                AssertEqual(BlackBoxNotFoundException.ErrorCode, noJob.Code, "404 code");

                using (DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false))
                    AssertTrue(await db.BlackBoxEntries.UpdateStateAsync(seeded.VoyageEntryId, BlackBoxStateEnum.Deleting).ConfigureAwait(false), "Deleting");
                // The voyage's current-entry lookup skips an entry being deleted (404); the entry read by id is 409.
                await ClientContract.ExpectErrorAsync(() => c.BlackBox.GetForVoyageAsync(seeded.VoyageId), "Deleting entry by voyage", 404, 404);
                ArmadaApiException deletingById = await ClientContract.ExpectErrorAsync(() => c.BlackBox.GetAsync(seeded.VoyageEntryId), "Deleting entry by id", 409, 409);
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
                        ArmadaApiException ex = await ClientContract.ExpectErrorAsync(() => client.BlackBox.GetAsync("bbx_stub"), "store down", 503, 503).ConfigureAwait(false);
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
