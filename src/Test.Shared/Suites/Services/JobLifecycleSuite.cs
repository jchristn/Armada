namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Background job housekeeping in <see cref="JobService"/>: the health loop's MaintainAsync fails jobs whose worker
    /// stopped reporting, and cancel is refused for a job that already finished.
    /// </summary>
    public sealed class JobLifecycleSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.JobLifecycle";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("maintain_fails_only_stale_running_jobs", "MaintainAsync fails Running jobs past the stale threshold and nothing else", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    JobService jobs = new JobService(testDb.Driver, Logging());
                    jobs.StaleRunningMinutes = 30;
                    Job stale = await CreateJobAsync(jobs, testDb, JobStatusEnum.Running, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                    Job fresh = await CreateJobAsync(jobs, testDb, JobStatusEnum.Running, DateTime.UtcNow.AddMinutes(-1)).ConfigureAwait(false);
                    Job oldQueued = await CreateJobAsync(jobs, testDb, JobStatusEnum.Queued, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                    Job done = await CreateJobAsync(jobs, testDb, JobStatusEnum.Succeeded, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);

                    await jobs.MaintainAsync().ConfigureAwait(false);

                    Job? staleAfter = await testDb.Driver.Jobs.ReadAsync(stale.Id).ConfigureAwait(false);
                    AssertEqual(JobStatusEnum.Failed, staleAfter!.Status, "stale running job failed");
                    AssertContains("did not report within 30 minutes", staleAfter.ErrorReason ?? "");
                    AssertNotNull(staleAfter.CompletedUtc);
                    AssertEqual(JobStatusEnum.Running, (await testDb.Driver.Jobs.ReadAsync(fresh.Id).ConfigureAwait(false))!.Status, "fresh running job untouched");
                    AssertEqual(JobStatusEnum.Queued, (await testDb.Driver.Jobs.ReadAsync(oldQueued.Id).ConfigureAwait(false))!.Status, "queued job untouched");
                    AssertEqual(JobStatusEnum.Succeeded, (await testDb.Driver.Jobs.ReadAsync(done.Id).ConfigureAwait(false))!.Status, "finished job untouched");
                }
            }));

            cases.Add(CaseAsync("cancel_finished_job_is_refused", "Cancelling a finished job throws and leaves it unchanged", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    JobService jobs = new JobService(testDb.Driver, Logging());
                    foreach (JobStatusEnum terminal in new JobStatusEnum[] { JobStatusEnum.Succeeded, JobStatusEnum.Failed, JobStatusEnum.Cancelled })
                    {
                        Job job = await CreateJobAsync(jobs, testDb, terminal, DateTime.UtcNow).ConfigureAwait(false);
                        await AssertThrowsAsync<InvalidOperationException>(() => jobs.CancelAsync(job), "cancel from " + terminal).ConfigureAwait(false);
                        AssertEqual(terminal, (await testDb.Driver.Jobs.ReadAsync(job.Id).ConfigureAwait(false))!.Status);
                    }

                    Job queued = await CreateJobAsync(jobs, testDb, JobStatusEnum.Queued, DateTime.UtcNow).ConfigureAwait(false);
                    Job cancelled = await jobs.CancelAsync(queued).ConfigureAwait(false);
                    AssertEqual(JobStatusEnum.Cancelled, cancelled.Status, "a queued job can be cancelled");
                }
            }));

            cases.Add(CaseAsync("cancel_with_stale_copy_keeps_first_terminal", "Cancelling from a stale Running copy of a job that has since finished is refused and keeps the finish", TestTags.Reliability, async () =>
            {
                // Regression: CancelAsync checked the caller's (stale) copy and rewrote the whole row, turning a job that
                // had already Succeeded into Cancelled.
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    JobService jobs = new JobService(testDb.Driver, Logging());
                    Job stale = await CreateJobAsync(jobs, testDb, JobStatusEnum.Running, DateTime.UtcNow).ConfigureAwait(false);
                    AssertTrue(await jobs.TryFinishAsync(stale.Id, JobStatusEnum.Succeeded, "{\"ok\":true}", null).ConfigureAwait(false), "worker finished first");

                    await AssertThrowsAsync<InvalidOperationException>(() => jobs.CancelAsync(stale), "cancel after finish").ConfigureAwait(false);
                    Job after = (await testDb.Driver.Jobs.ReadAsync(stale.Id).ConfigureAwait(false))!;
                    AssertEqual(JobStatusEnum.Succeeded, after.Status, "first terminal status wins");
                    AssertEqual(100, after.Progress);
                    AssertEqual("{\"ok\":true}", after.ResultJson);
                }
            }));

            cases.Add(CaseAsync("finish_and_heartbeat_after_cancel_are_no_ops", "After a cancel, heartbeats, finishes, and starts leave the job Cancelled", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    JobService jobs = new JobService(testDb.Driver, Logging());
                    Job running = await CreateJobAsync(jobs, testDb, JobStatusEnum.Running, DateTime.UtcNow.AddMinutes(-5)).ConfigureAwait(false);
                    Job cancelled = await jobs.CancelAsync(running).ConfigureAwait(false);
                    AssertEqual(JobStatusEnum.Cancelled, cancelled.Status);

                    AssertFalse(await jobs.HeartbeatAsync(running.Id, 90).ConfigureAwait(false), "heartbeat reports the job is no longer running");
                    AssertFalse(await jobs.TryFinishAsync(running.Id, JobStatusEnum.Succeeded, "{}", null).ConfigureAwait(false), "succeed after cancel");
                    AssertFalse(await jobs.TryFinishAsync(running.Id, JobStatusEnum.Failed, null, "late failure").ConfigureAwait(false), "fail after cancel");
                    AssertFalse(await jobs.TryStartAsync(running).ConfigureAwait(false), "start after cancel");

                    Job after = (await testDb.Driver.Jobs.ReadAsync(running.Id).ConfigureAwait(false))!;
                    AssertEqual(JobStatusEnum.Cancelled, after.Status);
                    AssertNull(after.ResultJson);
                    AssertNull(after.ErrorReason);
                    AssertEqual(cancelled.CompletedUtc, after.CompletedUtc, "completion time unchanged");
                    AssertTrue(after.Progress < 90, "heartbeat did not raise progress");

                    Job queued = await CreateJobAsync(jobs, testDb, JobStatusEnum.Queued, DateTime.UtcNow).ConfigureAwait(false);
                    AssertTrue(await jobs.TryStartAsync(queued, 5).ConfigureAwait(false), "a queued job starts");
                    AssertEqual(JobStatusEnum.Running, queued.Status);
                    AssertFalse(await jobs.TryStartAsync(queued).ConfigureAwait(false), "a job starts once");
                    AssertTrue(await jobs.HeartbeatAsync(queued.Id, 40).ConfigureAwait(false), "heartbeat on a running job");
                    AssertTrue(await jobs.HeartbeatAsync(queued.Id, 10).ConfigureAwait(false), "lower heartbeat progress");
                    AssertEqual(40, (await testDb.Driver.Jobs.ReadAsync(queued.Id).ConfigureAwait(false))!.Progress, "progress never lowered");
                }
            }));

            cases.Add(CaseAsync("maintain_does_not_overwrite_concurrent_finish", "The stale-job reaper leaves a job its worker finished after the reaper read it", TestTags.Reliability, async () =>
            {
                // Regression: MaintainAsync rewrote the whole row of a job it had read as stale Running, so a worker that
                // finished in between had its Succeeded replaced by Failed.
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    JobHookDatabaseDriver hooked = new JobHookDatabaseDriver(testDb.Driver);
                    JobService real = new JobService(testDb.Driver, Logging());
                    JobService reaper = new JobService(hooked, Logging());
                    reaper.StaleRunningMinutes = 30;
                    Job stale = await CreateJobAsync(real, testDb, JobStatusEnum.Running, DateTime.UtcNow.AddHours(-2)).ConfigureAwait(false);
                    int fired = 0;
                    hooked.HookedJobs.BeforeWriteAsync = async (string id, JobStatusEnum? status) =>
                    {
                        if (id != stale.Id || status != JobStatusEnum.Failed || Interlocked.Exchange(ref fired, 1) != 0) return;
                        await real.TryFinishAsync(id, JobStatusEnum.Succeeded, "{}", null).ConfigureAwait(false);
                    };

                    await reaper.MaintainAsync().ConfigureAwait(false);
                    AssertEqual(1, fired, "the worker finished inside the reaper's window");
                    Job after = (await testDb.Driver.Jobs.ReadAsync(stale.Id).ConfigureAwait(false))!;
                    AssertEqual(JobStatusEnum.Succeeded, after.Status, "first terminal status wins");
                    AssertNull(after.ErrorReason);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Job Lifecycle", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Job> CreateJobAsync(JobService jobs, TestDatabase testDb, JobStatusEnum status, DateTime lastUpdateUtc)
        {
            Job job = await jobs.EnqueueAsync("job " + status, JobKindEnum.Generic, Constants.DefaultTenantId, null).ConfigureAwait(false);
            job.Status = status;
            job.LastUpdateUtc = lastUpdateUtc;
            if (status == JobStatusEnum.Running) job.StartedUtc = lastUpdateUtc;
            return await testDb.Driver.Jobs.UpdateAsync(job).ConfigureAwait(false);
        }

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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
