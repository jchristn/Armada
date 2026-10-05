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
