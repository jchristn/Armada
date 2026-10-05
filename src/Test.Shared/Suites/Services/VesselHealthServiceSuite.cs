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
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the vessel health evaluator and service over a live database: exception isolation, manifest-hash
    /// freshness and max-age refresh of dependency checks, override set and remove recomputing effective columns, one
    /// evaluation job per tenant (409 semantics), the scheduler enqueuing once and not again while running, job
    /// cancellation, and an end-to-end evaluation where a failed dotnet list never grades Pass.
    /// </summary>
    public sealed class VesselHealthServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHealthService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("criterion_exception_is_isolated", "A throwing criterion grades Unknown (EvaluationError) and others still run", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                FakeHealthCriterion thrower = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.GitDivergence, Throw = true };
                FakeHealthCriterion ok = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes };
                VesselHealthEvaluator evaluator = CreateEvaluator(testDb.Driver, new ArmadaSettings(), thrower, ok);
                VesselHealth health = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.DivergenceStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.MissionOutcomeStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.OverallStatus);
                List<VesselHealthFinding> findings = await testDb.Driver.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertEqual(VesselHealthDetailCodes.EvaluationError, findings.Single(f => f.Criterion == VesselHealthCriterionEnum.GitDivergence).DetailCode);
                AssertEqual(1L, findings.Single(f => f.Criterion == VesselHealthCriterionEnum.MissionOutcomes).ValueA!.Value);
            }));

            cases.Add(CaseAsync("manifest_hash_skip_and_max_age_refresh", "Dependency checks skip while manifests are unchanged and fresh; rerun on change, age, or force", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                File.WriteAllText(Path.Combine(repo, "App.csproj"), "<Project />");
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repo).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.FetchBeforeEvaluate = false;
                settings.RepositoryHealth.DependencyMaxAgeHours = 24;
                FakeHealthCriterion deps = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.Dependencies, Status = VesselHealthStatusEnum.Warn, DetailCode = VesselHealthDetailCodes.OutdatedPackages };
                FakeHealthCriterion vulns = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.Vulnerabilities };
                FakeHealthCriterion git = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.GitDivergence };
                VesselHealthEvaluator evaluator = CreateEvaluator(testDb.Driver, settings, deps, vulns, git);
                DateTime now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
                evaluator.Clock = () => now;

                VesselHealth first = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(1, deps.Evaluations, "first evaluation runs dependency checks");
                AssertNotNull(first.ManifestHash, "manifest hash recorded");
                AssertNotNull(first.DependenciesEvaluatedUtc, "dependency timestamp recorded");

                now = now.AddHours(1);
                VesselHealth second = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(1, deps.Evaluations, "unchanged and fresh: skipped");
                AssertEqual(1, vulns.Evaluations, "vulnerabilities skipped too");
                AssertEqual(2, git.Evaluations, "git criteria always run");
                AssertEqual(VesselHealthStatusEnum.Warn, second.DependencyStatus, "prior finding kept");
                AssertEqual(first.ManifestHash, second.ManifestHash);
                List<VesselHealthFinding> kept = await testDb.Driver.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertEqual(VesselHealthDetailCodes.OutdatedPackages, kept.Single(f => f.Criterion == VesselHealthCriterionEnum.Dependencies).DetailCode);

                File.WriteAllText(Path.Combine(repo, "App.csproj"), "<Project Sdk=\"changed\" />");
                VesselHealth third = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(2, deps.Evaluations, "manifest change reruns");
                AssertNotEqual(first.ManifestHash, third.ManifestHash);

                now = now.AddHours(25);
                await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(3, deps.Evaluations, "older than DependencyMaxAgeHours reruns even with the same hash");

                now = now.AddMinutes(5);
                await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(3, deps.Evaluations, "fresh again: skipped");
                await evaluator.EvaluateAsync(vessel, true).ConfigureAwait(false);
                AssertEqual(4, deps.Evaluations, "force reruns");
            }));

            cases.Add(CaseAsync("dependency_tool_failure_retries_next_time", "A dependency tool failure leaves DependenciesEvaluatedUtc null so the next evaluation retries", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                File.WriteAllText(Path.Combine(repo, "App.csproj"), "<Project />");
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repo).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.FetchBeforeEvaluate = false;
                FakeHealthCriterion deps = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.Dependencies, Status = VesselHealthStatusEnum.Unknown, DetailCode = VesselHealthDetailCodes.Timeout };
                FakeHealthCriterion vulns = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.Vulnerabilities };
                VesselHealthEvaluator evaluator = CreateEvaluator(testDb.Driver, settings, deps, vulns);
                VesselHealth first = await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertNull(first.DependenciesEvaluatedUtc, "no freshness recorded after a failure");
                await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);
                AssertEqual(2, deps.Evaluations);
            }));

            cases.Add(CaseAsync("override_set_and_remove_recompute", "Setting and removing an override recomputes effective columns and the summary without re-evaluating", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                FakeHealthCriterion deps = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes, Status = VesselHealthStatusEnum.Fail };
                FakeHealthCriterion git = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.ArmadaReadiness, Status = VesselHealthStatusEnum.Pass };
                ArmadaSettings settings = new ArmadaSettings();
                VesselHealthEvaluator evaluator = CreateEvaluator(testDb.Driver, settings, deps, git);
                using VesselHealthService service = CreateService(testDb.Driver, settings, evaluator);
                await evaluator.EvaluateAsync(vessel, false).ConfigureAwait(false);

                VesselHealthSummary before = await service.GetSummaryAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(1L, before.Fail);

                VesselHealthDetail? set = await service.SetOverrideAsync(Constants.DefaultTenantId, vessel.Id, VesselHealthCriterionEnum.MissionOutcomes, VesselHealthStatusEnum.Pass, "known flaky", "usr_x").ConfigureAwait(false);
                AssertNotNull(set, "detail");
                AssertEqual(VesselHealthStatusEnum.Pass, set!.Health.MissionOutcomeStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, set.Health.OverallStatus);
                AssertEqual("known flaky", set.Overrides.Single().Note);
                AssertEqual(VesselHealthStatusEnum.Fail, set.Findings.Single(f => f.Criterion == VesselHealthCriterionEnum.MissionOutcomes).Status, "findings stay raw");
                AssertEqual(1, deps.Evaluations, "no re-evaluation");
                VesselHealthSummary during = await service.GetSummaryAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(0L, during.Fail, "summary is override-aware");
                AssertEqual(1L, during.Pass);

                VesselHealthDetail? overall = await service.SetOverrideAsync(Constants.DefaultTenantId, vessel.Id, VesselHealthCriterionEnum.Overall, VesselHealthStatusEnum.Warn, null, null).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Warn, overall!.Health.OverallStatus, "Overall override wins");

                await service.DeleteOverrideAsync(Constants.DefaultTenantId, vessel.Id, VesselHealthCriterionEnum.Overall).ConfigureAwait(false);
                VesselHealthDetail? removed = await service.DeleteOverrideAsync(Constants.DefaultTenantId, vessel.Id, VesselHealthCriterionEnum.MissionOutcomes).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Fail, removed!.Health.MissionOutcomeStatus);
                AssertEqual(VesselHealthStatusEnum.Fail, removed.Health.OverallStatus);
                AssertEqual(0, removed.Overrides.Count);

                AssertNull(await service.SetOverrideAsync("ten_other", vessel.Id, VesselHealthCriterionEnum.Overall, VesselHealthStatusEnum.Pass, null, null).ConfigureAwait(false), "cross-tenant is not found");
                AssertNull(await service.GetDetailAsync("ten_other", vessel.Id).ConfigureAwait(false), "cross-tenant detail is not found");
            }));

            cases.Add(CaseAsync("detail_for_unevaluated_vessel", "Detail for a never-evaluated vessel has a null Id and Unknown statuses", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings));
                VesselHealthDetail? detail = await service.GetDetailAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertNotNull(detail, "detail");
                AssertNull(detail!.Health.Id, "Id");
                AssertEqual(vessel.Name, detail.Health.VesselName);
                AssertEqual(VesselHealthStatusEnum.Unknown, detail.Health.OverallStatus);
                AssertEqual(0, detail.Findings.Count);
            }));

            cases.Add(CaseAsync("one_job_per_tenant_conflict", "A second evaluation request while one runs reports the running job (409)", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                FakeHealthCriterion blocker = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes, Gate = gate };
                ArmadaSettings settings = new ArmadaSettings();
                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings, blocker));

                VesselHealthEvaluationStart first = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, null, false).ConfigureAwait(false);
                AssertFalse(first.AlreadyRunning);
                AssertStartsWith(Constants.JobIdPrefix, first.JobId);
                VesselHealthEvaluationStart second = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, new VesselHealthEvaluateRequest { VesselIds = new List<string> { vessel.Id } }, false).ConfigureAwait(false);
                AssertTrue(second.AlreadyRunning);
                AssertEqual(first.JobId, second.JobId);

                gate.SetResult(true);
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Job? job = await testDb.Driver.Jobs.ReadAsync(first.JobId).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Succeeded, job!.Status);
                AssertEqual(JobKindEnum.Report, job.Kind);
                AssertEqual(100, job.Progress);
                VesselHealthJobResult healthResult = JsonHelper.Deserialize<VesselHealthJobResult>(job.ResultJson ?? "null");
                AssertNotNull(healthResult, "health job result");
                AssertEqual(1, healthResult.Evaluated, "evaluated count in the job result");

                VesselHealthEvaluationStart third = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, null, false).ConfigureAwait(false);
                AssertFalse(third.AlreadyRunning, "a new job can start after the first finishes");
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("unknown_vessel_or_fleet_rejected", "Unknown vessel or fleet identifiers are rejected before a job starts", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings));
                await AssertThrowsAsync<KeyNotFoundException>(() => service.StartEvaluationAsync(Constants.DefaultTenantId, null, new VesselHealthEvaluateRequest { VesselIds = new List<string> { "vsl_nope" } }, false));
                await AssertThrowsAsync<KeyNotFoundException>(() => service.StartEvaluationAsync(Constants.DefaultTenantId, null, new VesselHealthEvaluateRequest { FleetId = "flt_nope" }, false));
                AssertNull(service.GetRunningJobId(Constants.DefaultTenantId), "nothing running");
            }));

            cases.Add(CaseAsync("scheduler_enqueues_once_not_while_running", "With IntervalMinutes = 1 the scheduler enqueues once, not again while running, and again after the interval", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                FakeHealthCriterion blocker = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes, Gate = gate };
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.IntervalMinutes = 1;
                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings, blocker));
                DateTime now = DateTime.UtcNow;
                service.Clock = () => now;

                AssertEqual(1, await service.RunScheduleAsync().ConfigureAwait(false), "first tick enqueues");
                AssertEqual(0, await service.RunScheduleAsync().ConfigureAwait(false), "not again while running");
                now = now.AddMinutes(5);
                AssertEqual(0, await service.RunScheduleAsync().ConfigureAwait(false), "not again while running even when due");
                now = now.AddMinutes(-5).AddSeconds(30);
                AssertEqual(1, (await HealthJobsAsync(testDb.Driver)).Count);

                gate.SetResult(true);
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(0, await service.RunScheduleAsync().ConfigureAwait(false), "interval measured from the last start");
                now = now.AddMinutes(2);
                AssertEqual(1, await service.RunScheduleAsync().ConfigureAwait(false), "due again after the interval");
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(2, (await HealthJobsAsync(testDb.Driver)).Count);
            }));

            cases.Add(CaseAsync("scheduler_disabled_and_restart_aware", "IntervalMinutes = 0 disables the scheduler; a recent job in the database suppresses a run after restart", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.IntervalMinutes = 0;
                using VesselHealthService disabled = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings));
                AssertEqual(0, await disabled.RunScheduleAsync().ConfigureAwait(false));

                Job prior = new Job(VesselHealthService.JobName, JobKindEnum.Report) { TenantId = Constants.DefaultTenantId, Status = JobStatusEnum.Succeeded, CreatedUtc = DateTime.UtcNow.AddMinutes(-10) };
                await testDb.Driver.Jobs.CreateAsync(prior).ConfigureAwait(false);
                settings.RepositoryHealth.IntervalMinutes = 60;
                using VesselHealthService restarted = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings));
                AssertEqual(0, await restarted.RunScheduleAsync().ConfigureAwait(false), "last job 10 minutes ago, interval 60");
            }));

            cases.Add(CaseAsync("orphaned_job_failed_on_next_start", "A Running evaluation job left by a restart is failed when the next evaluation starts", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                JobService jobs = new JobService(testDb.Driver, CreateLogging());
                Job orphan = await jobs.EnqueueAsync(VesselHealthService.JobName, JobKindEnum.Report, Constants.DefaultTenantId, null).ConfigureAwait(false);
                orphan.Status = JobStatusEnum.Running;
                orphan.StartedUtc = DateTime.UtcNow.AddMinutes(-5);
                await testDb.Driver.Jobs.UpdateAsync(orphan).ConfigureAwait(false);

                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings, new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes }));
                VesselHealthEvaluationStart start = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, null, false).ConfigureAwait(false);
                AssertFalse(start.AlreadyRunning, "the orphan does not block a new evaluation");
                AssertNotEqual(orphan.Id, start.JobId);
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Job? failed = await testDb.Driver.Jobs.ReadAsync(orphan.Id).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Failed, failed!.Status, "orphan failed");
                AssertContains("interrupted", failed.ErrorReason ?? "");
                AssertNotNull(failed.CompletedUtc);
                Job? fresh = await testDb.Driver.Jobs.ReadAsync(start.JobId).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Succeeded, fresh!.Status, "new evaluation completes");
            }));

            cases.Add(CaseAsync("job_cancellation_stops_remaining", "Cancelling the job through JobService stops remaining vessels", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                for (int i = 0; i < 3; i++) await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                FakeHealthCriterion blocker = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes, Gate = gate };
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.MaxConcurrency = 1;
                using VesselHealthService service = CreateService(testDb.Driver, settings, CreateEvaluator(testDb.Driver, settings, blocker));
                service.CancellationPollInterval = TimeSpan.FromMilliseconds(100);
                VesselHealthEvaluationStart start = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, null, false).ConfigureAwait(false);

                JobService jobs = new JobService(testDb.Driver, CreateLogging());

                // Cancel only once the first vessel is known to be evaluating. Waiting for the job to read Running was
                // not enough: the job is marked Running before the first vessel starts, so a cancel that landed in
                // between stopped the run with no vessel started (0 evaluations instead of 1).
                await blocker.FirstEvaluationStarted.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                Job? job = await testDb.Driver.Jobs.ReadAsync(start.JobId).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Running, job!.Status, "job running while the first vessel evaluates");

                await jobs.CancelAsync(job).ConfigureAwait(false);
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(1, blocker.Evaluations, "only the first vessel started");
                Job? after = await testDb.Driver.Jobs.ReadAsync(start.JobId).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Cancelled, after!.Status);
            }));

            cases.Add(CaseAsync("cancel_during_progress_write_is_durable", "A cancel that lands inside a progress update stays Cancelled and is not overwritten by the finish", TestTags.Reliability, async () =>
            {
                // Regression: the progress update read the Running job and wrote the whole row back, so a cancel committed
                // between its read and write was overwritten with Running and the job then finished Succeeded.
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                JobHookDatabaseDriver hooked = new JobHookDatabaseDriver(testDb.Driver);
                for (int i = 0; i < 3; i++) await CreateVesselAsync(testDb.Driver, null).ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.MaxConcurrency = 1;
                FakeHealthCriterion criterion = new FakeHealthCriterion { Code = VesselHealthCriterionEnum.MissionOutcomes };
                using VesselHealthService service = CreateService(hooked, settings, CreateEvaluator(hooked, settings, criterion));
                service.CancellationPollInterval = TimeSpan.FromMilliseconds(50);
                JobCancelInjector injector = new JobCancelInjector(hooked, testDb.Driver, JobWriteMomentEnum.Heartbeat, j => j.Kind == JobKindEnum.Report);

                VesselHealthEvaluationStart start = await service.StartEvaluationAsync(Constants.DefaultTenantId, null, null, false).ConfigureAwait(false);
                Job injected = await injector.Injected.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                AssertEqual(start.JobId, injected.Id, "evaluation job cancelled");
                await service.WaitForIdleAsync(Constants.DefaultTenantId).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                Job? after = await testDb.Driver.Jobs.ReadAsync(start.JobId).ConfigureAwait(false);
                AssertEqual(JobStatusEnum.Cancelled, after!.Status, "job stays Cancelled");
                AssertEqual(injected.CompletedUtc, after.CompletedUtc, "the cancel is not rewritten");
            }));

            cases.Add(CaseAsync("end_to_end_failed_dotnet_list_never_pass", "Real criteria on a .NET repo: a failed dotnet list grades Unknown and the dependency columns stay null", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                File.WriteAllText(Path.Combine(repo, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Zzz.Missing\" Version=\"1.0.0\" /></ItemGroup></Project>");
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repo).ConfigureAwait(false);
                FakeHostCommandExecutor executor = new FakeHostCommandExecutor();
                executor.Handler = request => FakeHostCommandExecutor.Result(1, VesselHealthJsonFixtures.DotnetRestoreFailed);
                ArmadaSettings settings = new ArmadaSettings();
                settings.RepositoryHealth.FetchBeforeEvaluate = false;
                LoggingModule logging = CreateLogging();
                WorkflowProfileService profiles = new WorkflowProfileService(testDb.Driver, logging);
                VesselReadinessService readiness = new VesselReadinessService(testDb.Driver, profiles, logging);
                DependencyScanner scanner = new DependencyScanner(new DependencyToolRunner(executor));
                VesselHealthEvaluator evaluator = new VesselHealthEvaluator(testDb.Driver, new GitService(logging), settings,
                    VesselHealthEvaluator.CreateDefaultCriteria(testDb.Driver, readiness, scanner), logging);

                VesselHealth health = await evaluator.EvaluateAsync(vessel, true).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.DependencyStatus);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.VulnerabilityStatus);
                AssertNull(health.OutdatedCount, "no counts from a failed run");
                AssertEqual(VesselHealthStatusEnum.Warn, health.WorkingTreeStatus, "App.csproj is untracked");
                AssertEqual(1, health.ProjectCount!.Value);
                AssertEqual(VesselHealthStatusEnum.Fail, health.TestInfraStatus, "a .NET project without tests");
                List<VesselHealthFinding> findings = await testDb.Driver.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id).ConfigureAwait(false);
                AssertEqual(10, findings.Count, "one finding per criterion");
                AssertEqual(VesselHealthDetailCodes.RestoreRequired, findings.Single(f => f.Criterion == VesselHealthCriterionEnum.Dependencies).DetailCode);
                AssertTrue(executor.Requests.Count >= 2, "dotnet list ran for outdated and vulnerable");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static VesselHealthEvaluator CreateEvaluator(DatabaseDriver db, ArmadaSettings settings, params IVesselHealthCriterion[] criteria)
        {
            LoggingModule logging = CreateLogging();
            return new VesselHealthEvaluator(db, new GitService(logging), settings, criteria, logging);
        }

        private static VesselHealthService CreateService(DatabaseDriver db, ArmadaSettings settings, VesselHealthEvaluator evaluator)
        {
            LoggingModule logging = CreateLogging();
            return new VesselHealthService(db, settings, evaluator, new JobService(db, logging), logging);
        }

        private static async Task<Vessel> CreateVesselAsync(DatabaseDriver db, string? workingDirectory)
        {
            Vessel vessel = new Vessel("svc-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.com/svc.git");
            vessel.TenantId = Constants.DefaultTenantId;
            vessel.WorkingDirectory = workingDirectory;
            return await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<List<Job>> HealthJobsAsync(DatabaseDriver db)
        {
            List<Job> jobs = await db.Jobs.EnumerateAsync(Constants.DefaultTenantId).ConfigureAwait(false);
            return jobs.Where(j => j.Name == VesselHealthService.JobName).ToList();
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
