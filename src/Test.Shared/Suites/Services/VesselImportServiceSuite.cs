namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for vessel import execution: discover persists a batch, inline import creates vessels with
    /// WorkingDirectory set and LocalPath null, re-import is idempotent, unselected candidates are recorded,
    /// names are suffixed, existing vessels are matched, invalid requests are rejected, and selections above
    /// Import.InlineBatchLimit run as a background job. Also covers the shared VesselService.
    /// </summary>
    public sealed class VesselImportServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselImport";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the vessel import service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("discover_persists_batch", "Discover persists a Discovered batch with one item per candidate", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                MakeFakeRepo(Path.Combine(root, "one"));
                MakeFakeRepo(Path.Combine(root, "two"));
                VesselImportService service = NewService(testDb, root);

                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);
                AssertStartsWith("vib_", discovered.BatchId);
                AssertEqual(2, discovered.Candidates.Count);

                VesselImportBatchDetail? detail = await service.ReadBatchAsync(Constants.DefaultTenantId, discovered.BatchId).ConfigureAwait(false);
                AssertNotNull(detail, "batch detail");
                AssertEqual(VesselImportBatchStatusEnum.Discovered, detail!.Batch.Status);
                AssertEqual(2, detail.Batch.CandidateCount);
                AssertEqual(1, detail.Batch.RequestedPathCount);
                AssertEqual(2, detail.Items.Count);
                AssertEqual(0, (await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false)).Count, "no vessels created by discover");
            }));

            cases.Add(CaseAsync("inline_import_creates_vessels", "Inline import creates vessels with WorkingDirectory set and LocalPath null", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                string repo = MakeFakeRepo(Path.Combine(root, "alpha"));
                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                Fleet fleet = await testDb.Driver.Fleets.CreateAsync(new Fleet { TenantId = Constants.DefaultTenantId, Name = "import-fleet" }).ConfigureAwait(false);
                VesselImportRequest request = new VesselImportRequest();
                request.BatchId = discovered.BatchId;
                request.Paths = discovered.Candidates.Select(c => c.Path).ToList();
                request.FleetId = fleet.Id;
                request.Defaults = new VesselImportDefaults { LandingMode = LandingModeEnum.PullRequest, DefaultPipelineId = "ppl_test" };

                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, Constants.DefaultUserId, request).ConfigureAwait(false);
                AssertFalse(response.RunsInBackground, "inline");
                AssertEqual(VesselImportBatchStatusEnum.Completed, response.Batch!.Status);
                AssertEqual(1, response.Batch.CreatedCount);
                VesselImportItem item = response.Items.Single();
                AssertEqual(VesselImportOutcomeEnum.Created, item.Outcome);

                Vessel? vessel = await testDb.Driver.Vessels.ReadAsync(item.VesselId!).ConfigureAwait(false);
                AssertNotNull(vessel, "vessel");
                AssertEqual("alpha", vessel!.Name);
                AssertEqual(Norm(repo), vessel.WorkingDirectory);
                AssertEqual(Norm(repo), vessel.RepoUrl, "RepoUrl falls back to the local path");
                AssertNull(vessel.LocalPath, "LocalPath must stay null");
                AssertEqual(fleet.Id, vessel.FleetId);
                AssertEqual(LandingModeEnum.PullRequest, vessel.LandingMode);
                AssertEqual("ppl_test", vessel.DefaultPipelineId);
                AssertEqual(Constants.DefaultTenantId, vessel.TenantId);

                VesselImportBatch? stored = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                AssertNotNull(stored!.CompletedUtc, "CompletedUtc");
                AssertEqual(fleet.Id, stored.FleetId);
            }));

            cases.Add(CaseAsync("reimport_is_idempotent", "Importing the same batch twice creates no duplicates", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                MakeFakeRepo(Path.Combine(root, "beta"));
                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);
                VesselImportRequest request = SelectAll(discovered);

                await service.ImportAsync(Constants.DefaultTenantId, null, request).ConfigureAwait(false);
                VesselImportResponse second = await service.ImportAsync(Constants.DefaultTenantId, null, request).ConfigureAwait(false);
                AssertEqual(VesselImportOutcomeEnum.SkippedExisting, second.Items.Single().Outcome);
                AssertEqual(VesselImportCodes.VesselAlreadyExists, second.Items.Single().OutcomeReason);
                AssertEqual(0, second.Batch!.CreatedCount);
                AssertEqual(1, second.Batch.SkippedCount);
                AssertEqual(1, (await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false)).Count, "exactly one vessel");

                VesselImportDiscoverResponse rediscovered = await DiscoverRoot(service, root).ConfigureAwait(false);
                AssertEqual(VesselImportCandidateStatusEnum.AlreadyOnboarded, rediscovered.Candidates.Single().CandidateStatus);
            }));

            cases.Add(CaseAsync("unselected_recorded_as_skipped", "Candidates not selected are recorded as SkippedNotSelected", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                string keep = MakeFakeRepo(Path.Combine(root, "keep"));
                string leave = MakeFakeRepo(Path.Combine(root, "leave"));
                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                VesselImportRequest request = new VesselImportRequest();
                request.BatchId = discovered.BatchId;
                request.Paths = new List<string> { Norm(keep) };
                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, null, request).ConfigureAwait(false);

                AssertEqual(VesselImportOutcomeEnum.Created, response.Items.Single(i => i.Path == Norm(keep)).Outcome);
                VesselImportItem skipped = response.Items.Single(i => i.Path == Norm(leave));
                AssertEqual(VesselImportOutcomeEnum.SkippedNotSelected, skipped.Outcome);
                AssertEqual(VesselImportCodes.NotSelected, skipped.OutcomeReason);
                AssertEqual(1, response.Batch!.CreatedCount);
                AssertEqual(1, response.Batch.SkippedCount);
            }));

            cases.Add(CaseAsync("names_suffixed_and_existing_matched", "Colliding names get -2 and -3 suffixes, and existing vessels are matched by remote URL", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                MakeFakeRepo(Path.Combine(root, "a", "svc"));
                MakeFakeRepo(Path.Combine(root, "b", "svc"));
                string remoteRepo = Path.Combine(root, "c", "remote-one");
                Directory.CreateDirectory(Path.GetDirectoryName(remoteRepo)!);
                Directory.Move(TestGitRepoHelper.CreateWorkingRepoCopy(), remoteRepo);
                RunGit(remoteRepo, "remote", "add", "origin", "git@github.com:Example/Remote-One.git");

                await testDb.Driver.Vessels.CreateAsync(new Vessel { TenantId = Constants.DefaultTenantId, Name = "svc", RepoUrl = "https://example.com/svc.git" }).ConfigureAwait(false);
                Vessel remoteVessel = await testDb.Driver.Vessels.CreateAsync(new Vessel { TenantId = Constants.DefaultTenantId, Name = "remote", RepoUrl = "https://github.com/example/remote-one" }).ConfigureAwait(false);

                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);
                VesselImportItem matched = discovered.Candidates.Single(c => c.Path == Norm(remoteRepo));
                AssertEqual(VesselImportCandidateStatusEnum.AlreadyOnboarded, matched.CandidateStatus);
                AssertEqual(remoteVessel.Id, matched.ExistingVesselId);

                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, null, SelectAll(discovered)).ConfigureAwait(false);
                List<string> created = new List<string>();
                foreach (VesselImportItem item in response.Items.Where(i => i.Outcome == VesselImportOutcomeEnum.Created))
                {
                    Vessel? vessel = await testDb.Driver.Vessels.ReadAsync(item.VesselId!).ConfigureAwait(false);
                    created.Add(vessel!.Name);
                }

                created.Sort(StringComparer.Ordinal);
                AssertEqual(2, created.Count);
                AssertEqual("svc-2", created[0]);
                AssertEqual("svc-3", created[1]);
                AssertEqual(VesselImportOutcomeEnum.SkippedExisting, response.Items.Single(i => i.Path == Norm(remoteRepo)).Outcome);
            }));

            cases.Add(CaseAsync("invalid_requests_rejected", "Missing input, unknown paths, unknown fleets, and other tenants' batches are rejected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                MakeFakeRepo(Path.Combine(root, "gamma"));
                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                VesselImportRequest empty = new VesselImportRequest { BatchId = discovered.BatchId };
                await AssertThrowsAsync<ArgumentException>(() => service.ImportAsync(Constants.DefaultTenantId, null, empty), "empty paths").ConfigureAwait(false);

                VesselImportRequest unknown = new VesselImportRequest { BatchId = discovered.BatchId, Paths = new List<string> { "/not/in/batch" } };
                await AssertThrowsAsync<ArgumentException>(() => service.ImportAsync(Constants.DefaultTenantId, null, unknown), "unknown path").ConfigureAwait(false);

                VesselImportRequest badFleet = SelectAll(discovered);
                badFleet.FleetId = "flt_missing";
                await AssertThrowsAsync<ArgumentException>(() => service.ImportAsync(Constants.DefaultTenantId, null, badFleet), "unknown fleet").ConfigureAwait(false);

                TenantMetadata other = new TenantMetadata("Import Other " + Guid.NewGuid().ToString("N").Substring(0, 6));
                await testDb.Driver.Tenants.CreateAsync(other).ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => service.ImportAsync(other.Id, null, SelectAll(discovered)), "cross-tenant batch").ConfigureAwait(false);
                AssertNull(await service.ReadBatchAsync(other.Id, discovered.BatchId).ConfigureAwait(false), "cross-tenant read");
                AssertEqual(0L, (await service.EnumerateBatchesAsync(other.Id, null).ConfigureAwait(false)).TotalRecords, "cross-tenant enumerate");
                AssertEqual(1L, (await service.EnumerateBatchesAsync(Constants.DefaultTenantId, null).ConfigureAwait(false)).TotalRecords, "own enumerate");
            }));

            cases.Add(CaseAsync("large_selection_runs_as_job", "A selection above InlineBatchLimit runs as a background job and the batch reflects final counts", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                for (int i = 0; i < 12; i++) MakeFakeRepo(Path.Combine(root, "job-repo-" + i.ToString("D2")));
                ArmadaSettings settings = NewSettings(root);
                settings.Import.InlineBatchLimit = 5;
                VesselImportService service = NewService(testDb, settings);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, Constants.DefaultUserId, SelectAll(discovered)).ConfigureAwait(false);
                AssertTrue(response.RunsInBackground, "background");
                AssertStartsWith("job_", response.JobId ?? "", "job id");
                AssertEqual(0, response.Items.Count, "no items in 202 response");

                VesselImportBatch? batch = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    batch = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                    if (batch != null && batch.Status != VesselImportBatchStatusEnum.Importing) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }

                AssertEqual(VesselImportBatchStatusEnum.Completed, batch!.Status);
                AssertEqual(12, batch.CreatedCount);
                AssertEqual(response.JobId, batch.JobId);
                Job? job = await JobWait.ForTerminalAsync(testDb.Driver, response.JobId!).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Succeeded, job!.Status);
                VesselImportJobSummary importSummary = JsonHelper.Deserialize<VesselImportJobSummary>(job.ResultJson ?? "null");
                AssertNotNull(importSummary, "import job result");
                AssertEqual(12, importSummary.CreatedCount, "created count in the job result");
                AssertEqual(batch.Id, importSummary.BatchId, "batch id in the job result");
                AssertEqual(12, (await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("cancel_import_job_mid_batch", "Cancelling a background import stops it part way; unprocessed items are marked cancelled", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                for (int i = 0; i < 25; i++) MakeFakeRepo(Path.Combine(root, "cancel-repo-" + i.ToString("D2")));
                ArmadaSettings settings = NewSettings(root);
                settings.Import.InlineBatchLimit = 5;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                GatedVesselService gated = new GatedVesselService(new VesselService(testDb.Driver), 3);
                JobService jobs = new JobService(testDb.Driver, logging);
                VesselImportService service = new VesselImportService(testDb.Driver, settings, new VesselDiscoveryService(testDb.Driver, settings), gated, jobs, logging);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, Constants.DefaultUserId, SelectAll(discovered)).ConfigureAwait(false);
                AssertTrue(response.RunsInBackground, "background job");
                await gated.Blocked.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Job? running = await testDb.Driver.Jobs.ReadAsync(response.JobId!).ConfigureAwait(false);
                await jobs.CancelAsync(running!).ConfigureAwait(false);
                gated.Release();

                VesselImportBatch? batch = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    batch = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                    if (batch != null && batch.Status != VesselImportBatchStatusEnum.Importing) break;
                    await Task.Delay(50).ConfigureAwait(false);
                }

                AssertEqual(VesselImportBatchStatusEnum.Failed, batch!.Status, "a cancelled import ends Failed");
                AssertTrue(batch.CreatedCount > 0 && batch.CreatedCount < 25, "stopped part way, created " + batch.CreatedCount);
                Job? after = await JobWait.ForTerminalAsync(testDb.Driver, response.JobId!).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Cancelled, after!.Status);
                List<VesselImportItem> items = await testDb.Driver.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, discovered.BatchId).ConfigureAwait(false);
                int cancelledItems = items.Count(i => i.OutcomeReason == VesselImportCodes.Cancelled);
                AssertEqual(25 - batch.CreatedCount, cancelledItems, "every unprocessed selected item says why");
                AssertEqual(batch.CreatedCount, (await testDb.Driver.Vessels.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false)).Count, "vessels created before the cancel are kept");
            }));

            cases.Add(CaseAsync("cancel_during_progress_write_is_durable", "A cancel that lands inside an import progress update stays Cancelled and stops the import", TestTags.Reliability, async () =>
            {
                // Regression: the progress update read the Running job and wrote the whole row back, so a cancel committed
                // between its read and write was overwritten with Running and the import ran to the end as Succeeded.
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                JobHookDatabaseDriver hooked = new JobHookDatabaseDriver(testDb.Driver);
                string root = TestTemp.NewDirectory("import");
                for (int i = 0; i < 25; i++) MakeFakeRepo(Path.Combine(root, "race-repo-" + i.ToString("D2")));
                ArmadaSettings settings = NewSettings(root);
                settings.Import.InlineBatchLimit = 5;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                VesselImportService service = new VesselImportService(hooked, settings, new VesselDiscoveryService(hooked, settings), new VesselService(hooked), new JobService(hooked, logging), logging);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);
                JobCancelInjector injector = new JobCancelInjector(hooked, testDb.Driver, JobWriteMomentEnum.Heartbeat, j => j.Kind == JobKindEnum.VesselImport);

                VesselImportResponse response = await service.ImportAsync(Constants.DefaultTenantId, Constants.DefaultUserId, SelectAll(discovered)).ConfigureAwait(false);
                AssertTrue(response.RunsInBackground, "background job");
                Job injected = await injector.Injected.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(response.JobId, injected.Id, "import job cancelled");

                // The job is Cancelled from the moment of the cancel, so wait for the worker to finish the batch first.
                VesselImportBatch? batch = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (!deadline.Passed)
                {
                    batch = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                    if (batch != null && batch.Status != VesselImportBatchStatusEnum.Importing) break;
                    await Task.Delay(50).ConfigureAwait(false);
                }

                AssertEqual(VesselImportBatchStatusEnum.Failed, batch!.Status, "a cancelled import ends Failed");
                AssertTrue(batch.CreatedCount < 25, "stopped part way, created " + batch.CreatedCount);
                Job after = (await testDb.Driver.Jobs.ReadAsync(response.JobId!).ConfigureAwait(false))!;
                AssertEqual(JobStatusEnum.Cancelled, after.Status, "job stays Cancelled");
                AssertEqual(injected.CompletedUtc, after.CompletedUtc, "the cancel is not rewritten");
                List<VesselImportItem> items = await testDb.Driver.VesselImportItems.EnumerateByBatchAsync(Constants.DefaultTenantId, discovered.BatchId).ConfigureAwait(false);
                AssertEqual(25 - batch.CreatedCount, items.Count(i => i.OutcomeReason == VesselImportCodes.Cancelled), "every unprocessed selected item says why");
            }));

            cases.Add(CaseAsync("restart_fails_orphaned_import", "An import left Importing by a restart is failed at startup and its job too", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("import");
                for (int i = 0; i < 3; i++) MakeFakeRepo(Path.Combine(root, "orphan-repo-" + i.ToString("D2")));
                VesselImportService service = NewService(testDb, root);
                VesselImportDiscoverResponse discovered = await DiscoverRoot(service, root).ConfigureAwait(false);

                // Simulate the state a crash leaves behind: the batch Importing with a Running job.
                JobService jobs = new JobService(testDb.Driver, new LoggingModule());
                Job job = await jobs.EnqueueAsync("Vessel import " + discovered.BatchId, JobKindEnum.VesselImport, Constants.DefaultTenantId, Constants.DefaultUserId).ConfigureAwait(false);
                job.Status = JobStatusEnum.Running;
                await testDb.Driver.Jobs.UpdateAsync(job).ConfigureAwait(false);
                VesselImportBatch? batch = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                batch!.Status = VesselImportBatchStatusEnum.Importing;
                batch.JobId = job.Id;
                await testDb.Driver.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);

                await NewService(testDb, root).RecoverAsync().ConfigureAwait(false);

                VesselImportBatch? recovered = await testDb.Driver.VesselImportBatches.ReadAsync(discovered.BatchId).ConfigureAwait(false);
                AssertEqual(VesselImportBatchStatusEnum.Failed, recovered!.Status, "no batch stays Importing forever");
                AssertContains("restarted", recovered.ErrorMessage ?? "");
                AssertNotNull(recovered.CompletedUtc);
                Job? failedJob = await testDb.Driver.Jobs.ReadAsync(job.Id).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Failed, failedJob!.Status);
            }));

            cases.Add(CaseAsync("vessel_service_validates_and_infers", "VesselService requires RepoUrl, infers WorkingDirectory only when asked, and never sets LocalPath", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                VesselService vessels = new VesselService(testDb.Driver);
                await AssertThrowsAsync<ArgumentException>(() => vessels.CreateAsync(new Vessel { Name = "no-url" }), "missing RepoUrl").ConfigureAwait(false);

                string clone = TestGitRepoHelper.CreateWorkingRepoCopy();
                Vessel inferred = await vessels.CreateAsync(new Vessel { TenantId = Constants.DefaultTenantId, Name = "inferred", RepoUrl = clone }, true).ConfigureAwait(false);
                AssertEqual(Path.GetFullPath(clone), inferred.WorkingDirectory);
                AssertNull(inferred.LocalPath, "LocalPath stays null for a local-path RepoUrl");

                Vessel plain = await vessels.CreateAsync(new Vessel { TenantId = Constants.DefaultTenantId, Name = "plain", RepoUrl = clone }, false).ConfigureAwait(false);
                AssertNull(plain.WorkingDirectory, "no inference unless requested (REST behavior)");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Import Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings NewSettings(string allowedRoot)
        {
            ArmadaSettings settings = new ArmadaSettings();
            string data = TestTemp.NewDirectory("import_data");
            settings.DataDirectory = data;
            settings.ReposDirectory = Path.Combine(data, "repos");
            settings.DocksDirectory = Path.Combine(data, "docks");
            settings.Import.AllowedRoots = new List<string> { allowedRoot };
            return settings;
        }

        private static VesselImportService NewService(TestDatabase testDb, string allowedRoot)
        {
            return NewService(testDb, NewSettings(allowedRoot));
        }

        private static VesselImportService NewService(TestDatabase testDb, ArmadaSettings settings)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            DatabaseDriver db = testDb.Driver;
            return new VesselImportService(db, settings, new VesselDiscoveryService(db, settings), new VesselService(db), new JobService(db, logging), logging);
        }

        private static Task<VesselImportDiscoverResponse> DiscoverRoot(VesselImportService service, string root)
        {
            VesselDiscoveryRequest request = new VesselDiscoveryRequest();
            request.Roots = new List<string> { root };
            return service.DiscoverAsync(Constants.DefaultTenantId, Constants.DefaultUserId, request);
        }

        private static VesselImportRequest SelectAll(VesselImportDiscoverResponse discovered)
        {
            VesselImportRequest request = new VesselImportRequest();
            request.BatchId = discovered.BatchId;
            request.Paths = discovered.Candidates.Select(c => c.Path).ToList();
            return request;
        }

        private static string MakeFakeRepo(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            return path;
        }

        private static string Norm(string path)
        {
            return VesselImportPaths.NormalizeInputPath(path);
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

            using System.Diagnostics.Process process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();
            process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed: " + standardError.Trim());
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
