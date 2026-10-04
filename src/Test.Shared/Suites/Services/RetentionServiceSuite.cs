namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
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
    /// Retention (V1 readiness W3.4): <see cref="RetentionService"/> archives and deletes inactive Ask threads (never
    /// pinned ones; deletion removes messages), deletes finished jobs past retention while keeping the newest of each
    /// kind per tenant, and deletes finished import batches with their items while keeping in-progress or still
    /// categorizing ones; 0 disables each rule. Also verifies fleet action pruning removes run output and that vessel
    /// health findings are current-state only (re-evaluation replaces them). Runs on the configured provider.
    /// </summary>
    public sealed class RetentionServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.Retention";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Retention suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("settings_defaults_and_clamp", "Retention settings default to archive 90, delete never, jobs 30, imports 90 and clamp to 0..3650", ct =>
            {
                RetentionSettings settings = new RetentionSettings();
                AssertEqual(90, settings.AskThreadArchiveAfterDays);
                AssertEqual(0, settings.AskThreadDeleteAfterDays);
                AssertEqual(30, settings.JobRetentionDays);
                AssertEqual(90, settings.ImportBatchRetentionDays);
                settings.AskThreadArchiveAfterDays = -1;
                AssertEqual(0, settings.AskThreadArchiveAfterDays, "negative clamps to 0 (never)");
                settings.JobRetentionDays = 99999;
                AssertEqual(3650, settings.JobRetentionDays, "maximum 3650");
                ArmadaSettings armada = new ArmadaSettings();
                armada.Retention = null!;
                AssertNotNull(armada.Retention, "null restores defaults");
                return Task.CompletedTask;
            }));

            cases.Add(Case("archive_inactive_ask_threads", "Inactive unpinned Ask threads are archived; pinned and active threads are not", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime now = DateTime.UtcNow;
                    AskThread stale = await CreateThreadAsync(db, "stale", now.AddDays(-100), now.AddDays(-120), false).ConfigureAwait(false);
                    AskThread staleEmpty = await CreateThreadAsync(db, "stale-empty", null, now.AddDays(-95), false).ConfigureAwait(false);
                    AskThread pinned = await CreateThreadAsync(db, "pinned", now.AddDays(-300), now.AddDays(-300), true).ConfigureAwait(false);
                    AskThread active = await CreateThreadAsync(db, "active", now.AddDays(-5), now.AddDays(-200), false).ConfigureAwait(false);

                    ArmadaSettings settings = new ArmadaSettings();
                    RetentionService service = new RetentionService(db, settings, QuietLogging());
                    AssertEqual(2, await service.ArchiveInactiveAskThreadsAsync(ct).ConfigureAwait(false), "two archived at the 90-day default");

                    AssertTrue((await db.AskThreads.ReadByIdAsync(stale.Id, ct).ConfigureAwait(false))!.Archived, "stale archived");
                    AssertTrue((await db.AskThreads.ReadByIdAsync(staleEmpty.Id, ct).ConfigureAwait(false))!.Archived, "stale empty thread archived by creation time");
                    AssertFalse((await db.AskThreads.ReadByIdAsync(pinned.Id, ct).ConfigureAwait(false))!.Archived, "pinned never archived");
                    AssertFalse((await db.AskThreads.ReadByIdAsync(active.Id, ct).ConfigureAwait(false))!.Archived, "recent activity keeps the thread");
                    AssertEqual(0, await service.ArchiveInactiveAskThreadsAsync(ct).ConfigureAwait(false), "second pass archives nothing");

                    settings.Retention.AskThreadArchiveAfterDays = 0;
                    AskThread another = await CreateThreadAsync(db, "another-stale", now.AddDays(-400), now.AddDays(-400), false).ConfigureAwait(false);
                    AssertEqual(0, await service.ArchiveInactiveAskThreadsAsync(ct).ConfigureAwait(false), "0 disables archiving");
                    AssertFalse((await db.AskThreads.ReadByIdAsync(another.Id, ct).ConfigureAwait(false))!.Archived, "untouched when disabled");
                }
            }));

            cases.Add(Case("delete_inactive_ask_threads_with_messages", "Ask thread deletion removes inactive threads and their messages; pinned threads stay", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime now = DateTime.UtcNow;
                    AskThread old = await CreateThreadAsync(db, "old", null, now, false).ConfigureAwait(false);
                    AskMessage message = await AddMessageAsync(db, old, "hello").ConfigureAwait(false);
                    AskThread pinned = await CreateThreadAsync(db, "pinned-old", null, now, true).ConfigureAwait(false);
                    await AddMessageAsync(db, pinned, "keep me").ConfigureAwait(false);
                    AskThread future = await CreateThreadAsync(db, "recent-relative", now.AddDays(150), now, false).ConfigureAwait(false);

                    ArmadaSettings settings = new ArmadaSettings();
                    RetentionService disabled = new RetentionService(db, settings, QuietLogging(), () => now.AddDays(1000));
                    AssertEqual(0, await disabled.DeleteInactiveAskThreadsAsync(ct).ConfigureAwait(false), "deletion is off by default");

                    // 200 days from now, with deletion after 180 days: the threads active "now" are 200 days idle.
                    settings.Retention.AskThreadDeleteAfterDays = 180;
                    RetentionService service = new RetentionService(db, settings, QuietLogging(), () => now.AddDays(200));
                    AssertEqual(1, await service.DeleteInactiveAskThreadsAsync(ct).ConfigureAwait(false), "one thread deleted");

                    AssertNull(await db.AskThreads.ReadByIdAsync(old.Id, ct).ConfigureAwait(false), "idle thread deleted");
                    AssertNull(await db.AskMessages.ReadAsync(Constants.DefaultTenantId, message.Id, ct).ConfigureAwait(false), "its messages deleted");
                    AssertNotNull(await db.AskThreads.ReadByIdAsync(pinned.Id, ct).ConfigureAwait(false), "pinned thread kept");
                    AssertNotNull(await db.AskThreads.ReadByIdAsync(future.Id, ct).ConfigureAwait(false), "thread active within the window kept");
                }
            }));

            cases.Add(Case("prune_finished_jobs", "Finished jobs past retention are deleted; running jobs and the newest finished job per kind and name are kept", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime now = DateTime.UtcNow;
                    Job oldSucceeded = await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Succeeded, now.AddDays(-60)).ConfigureAwait(false);
                    Job oldFailed = await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Failed, now.AddDays(-45)).ConfigureAwait(false);
                    Job newestGeneric = await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Cancelled, now.AddDays(-40)).ConfigureAwait(false);
                    Job onlyOfKind = await CreateJobAsync(db, JobKindEnum.Report, JobStatusEnum.Succeeded, now.AddDays(-90)).ConfigureAwait(false);
                    Job running = await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Running, null, now.AddDays(-90)).ConfigureAwait(false);
                    Job recent = await CreateJobAsync(db, JobKindEnum.VesselImport, JobStatusEnum.Succeeded, now.AddDays(-1)).ConfigureAwait(false);
                    Job recentOlderSameKind = await CreateJobAsync(db, JobKindEnum.VesselImport, JobStatusEnum.Succeeded, now.AddDays(-50)).ConfigureAwait(false);

                    ArmadaSettings settings = new ArmadaSettings();
                    RetentionService service = new RetentionService(db, settings, QuietLogging());
                    AssertEqual(3, await service.PruneJobsAsync(ct).ConfigureAwait(false), "three jobs past the 30-day default");

                    AssertNull(await db.Jobs.ReadAsync(oldSucceeded.Id, ct).ConfigureAwait(false), "old succeeded deleted");
                    AssertNull(await db.Jobs.ReadAsync(oldFailed.Id, ct).ConfigureAwait(false), "old failed deleted");
                    AssertNull(await db.Jobs.ReadAsync(recentOlderSameKind.Id, ct).ConfigureAwait(false), "old job deleted when a newer one of its kind exists");
                    AssertNotNull(await db.Jobs.ReadAsync(newestGeneric.Id, ct).ConfigureAwait(false), "newest finished job of the kind kept");
                    AssertNotNull(await db.Jobs.ReadAsync(onlyOfKind.Id, ct).ConfigureAwait(false), "only job of its kind kept (schedule anchor)");
                    AssertNotNull(await db.Jobs.ReadAsync(running.Id, ct).ConfigureAwait(false), "running job kept");
                    AssertNotNull(await db.Jobs.ReadAsync(recent.Id, ct).ConfigureAwait(false), "recent job kept");

                    Job otherName = new Job { TenantId = Constants.DefaultTenantId, Name = "another generic job", Kind = JobKindEnum.Generic, Status = JobStatusEnum.Succeeded, CreatedUtc = now.AddDays(-70), CompletedUtc = now.AddDays(-70), LastUpdateUtc = now.AddDays(-70) };
                    otherName = await db.Jobs.CreateAsync(otherName, ct).ConfigureAwait(false);
                    AssertEqual(0, await service.PruneJobsAsync(ct).ConfigureAwait(false), "the only job with its name is kept even when old");

                    settings.Retention.JobRetentionDays = 0;
                    await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Succeeded, now.AddDays(-500)).ConfigureAwait(false);
                    AssertEqual(0, await service.PruneJobsAsync(ct).ConfigureAwait(false), "0 disables job pruning");
                }
            }));

            cases.Add(Case("prune_finished_import_batches", "Finished import batches past retention are deleted with their items; in-progress and categorizing batches are kept", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime now = DateTime.UtcNow;
                    VesselImportBatch completed = await CreateBatchAsync(db, VesselImportBatchStatusEnum.Completed, VesselImportCategorizationStatusEnum.None, now.AddDays(-1)).ConfigureAwait(false);
                    VesselImportItem item = await db.VesselImportItems.CreateAsync(new VesselImportItem
                    {
                        TenantId = Constants.DefaultTenantId,
                        BatchId = completed.Id,
                        Path = "/repos/retention-sample",
                        ProposedName = "retention-sample"
                    }, ct).ConfigureAwait(false);
                    VesselImportBatch failed = await CreateBatchAsync(db, VesselImportBatchStatusEnum.Failed, VesselImportCategorizationStatusEnum.None, now.AddDays(-1)).ConfigureAwait(false);
                    VesselImportBatch partial = await CreateBatchAsync(db, VesselImportBatchStatusEnum.CompletedWithFailures, VesselImportCategorizationStatusEnum.Applied, now.AddDays(-1)).ConfigureAwait(false);
                    VesselImportBatch importing = await CreateBatchAsync(db, VesselImportBatchStatusEnum.Importing, VesselImportCategorizationStatusEnum.None, null).ConfigureAwait(false);
                    VesselImportBatch categorizing = await CreateBatchAsync(db, VesselImportBatchStatusEnum.Completed, VesselImportCategorizationStatusEnum.Running, now.AddDays(-1)).ConfigureAwait(false);

                    ArmadaSettings settings = new ArmadaSettings();
                    RetentionService soon = new RetentionService(db, settings, QuietLogging(), () => now.AddDays(50));
                    AssertEqual(0, await soon.PruneImportBatchesAsync(ct).ConfigureAwait(false), "nothing is 90 days old yet");

                    RetentionService later = new RetentionService(db, settings, QuietLogging(), () => now.AddDays(100));
                    AssertEqual(3, await later.PruneImportBatchesAsync(ct).ConfigureAwait(false), "three finished batches deleted");
                    AssertNull(await db.VesselImportBatches.ReadAsync(completed.Id, ct).ConfigureAwait(false), "completed batch deleted");
                    AssertNull(await db.VesselImportItems.ReadAsync(item.Id, ct).ConfigureAwait(false), "its items deleted");
                    AssertNull(await db.VesselImportBatches.ReadAsync(failed.Id, ct).ConfigureAwait(false), "failed batch deleted");
                    AssertNull(await db.VesselImportBatches.ReadAsync(partial.Id, ct).ConfigureAwait(false), "partially failed batch deleted");
                    AssertNotNull(await db.VesselImportBatches.ReadAsync(importing.Id, ct).ConfigureAwait(false), "in-progress batch kept");
                    AssertNotNull(await db.VesselImportBatches.ReadAsync(categorizing.Id, ct).ConfigureAwait(false), "batch still categorizing kept");

                    settings.Retention.ImportBatchRetentionDays = 0;
                    RetentionService disabled = new RetentionService(db, settings, QuietLogging(), () => now.AddDays(5000));
                    AssertEqual(0, await disabled.PruneImportBatchesAsync(ct).ConfigureAwait(false), "0 disables import pruning");
                }
            }));

            cases.Add(Case("prune_pass_runs_all_rules", "One PruneAsync pass applies every rule and reports counts", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    DateTime now = DateTime.UtcNow;
                    await CreateThreadAsync(db, "pass-stale", now.AddDays(-100), now.AddDays(-100), false).ConfigureAwait(false);
                    await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Succeeded, now.AddDays(-60)).ConfigureAwait(false);
                    await CreateJobAsync(db, JobKindEnum.Generic, JobStatusEnum.Succeeded, now.AddDays(-50)).ConfigureAwait(false);

                    RetentionResult result = await new RetentionService(db, new ArmadaSettings(), QuietLogging()).PruneAsync(ct).ConfigureAwait(false);
                    AssertEqual(1, result.AskThreadsArchived, "archived");
                    AssertEqual(0, result.AskThreadsDeleted, "deletion off by default");
                    AssertEqual(1, result.JobsDeleted, "older of the two jobs deleted");
                    AssertEqual(2, result.Total, "total");
                }
            }));

            cases.Add(Case("fleet_action_prune_removes_output", "Fleet action run pruning deletes the run's targets and their captured output", async ct =>
            {
                using (FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false))
                {
                    h.Settings.FleetActions.RunRetentionDays = 1;
                    FleetActionRun run = await h.Db.Driver.FleetActionRuns.CreateAsync(new FleetActionRun
                    {
                        TenantId = Constants.DefaultTenantId, ActionName = "old-with-output", Status = FleetActionRunStatusEnum.Completed,
                        CreatedUtc = DateTime.UtcNow.AddDays(-5), CompletedUtc = DateTime.UtcNow.AddDays(-5)
                    }, ct).ConfigureAwait(false);
                    await h.Db.Driver.FleetActionRunTargets.CreateAsync(new FleetActionRunTarget
                    {
                        TenantId = Constants.DefaultTenantId, RunId = run.Id, VesselId = "vsl_retention", VesselName = "retention",
                        Status = FleetActionTargetStatusEnum.Succeeded, OutputText = "captured stdout", ErrorText = "captured stderr"
                    }, ct).ConfigureAwait(false);
                    AssertEqual(1, (await h.Db.Driver.FleetActionRunTargets.ReadAllByRunAsync(run.Id, ct).ConfigureAwait(false)).Count, "target with output exists");

                    AssertEqual(1, await h.Runner.PruneExpiredRunsAsync(ct).ConfigureAwait(false), "run pruned");
                    AssertNull(await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id, ct).ConfigureAwait(false), "run gone");
                    AssertEqual(0, (await h.Db.Driver.FleetActionRunTargets.ReadAllByRunAsync(run.Id, ct).ConfigureAwait(false)).Count, "targets and their output gone");
                }
            }));

            cases.Add(Case("health_findings_do_not_accumulate", "Vessel health findings are current-state only: re-evaluation replaces them instead of adding history", async ct =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("retention-health", "https://example.invalid/health.git");
                    vessel.TenantId = Constants.DefaultTenantId;
                    vessel = await db.Vessels.CreateAsync(vessel, ct).ConfigureAwait(false);

                    for (int evaluation = 0; evaluation < 3; evaluation++)
                    {
                        List<VesselHealthFinding> findings = new List<VesselHealthFinding>
                        {
                            new VesselHealthFinding { TenantId = Constants.DefaultTenantId, VesselId = vessel.Id, Criterion = VesselHealthCriterionEnum.Overall, Status = VesselHealthStatusEnum.Pass },
                            new VesselHealthFinding { TenantId = Constants.DefaultTenantId, VesselId = vessel.Id, Criterion = VesselHealthCriterionEnum.GitDivergence, Status = VesselHealthStatusEnum.Warn, ValueA = evaluation }
                        };
                        await db.VesselHealthFindings.ReplaceForVesselAsync(Constants.DefaultTenantId, vessel.Id, findings, ct).ConfigureAwait(false);
                    }

                    List<VesselHealthFinding> stored = await db.VesselHealthFindings.ReadByVesselAsync(Constants.DefaultTenantId, vessel.Id, ct).ConfigureAwait(false);
                    AssertEqual(2, stored.Count, "only the latest evaluation's findings are stored");
                    AssertEqual(2L, stored.First(f => f.Criterion == VesselHealthCriterionEnum.GitDivergence).ValueA ?? -1L, "latest values kept");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Retention",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static async Task<AskThread> CreateThreadAsync(DatabaseDriver db, string title, DateTime? lastMessageUtc, DateTime createdUtc, bool pinned)
        {
            AskThread thread = new AskThread();
            thread.TenantId = Constants.DefaultTenantId;
            thread.UserId = Constants.DefaultUserId;
            thread.Title = title;
            thread.Pinned = pinned;
            thread.LastMessageUtc = lastMessageUtc;
            thread.CreatedUtc = createdUtc;
            return await db.AskThreads.CreateAsync(thread).ConfigureAwait(false);
        }

        private static async Task<AskMessage> AddMessageAsync(DatabaseDriver db, AskThread thread, string text)
        {
            AskMessage message = new AskMessage();
            message.TenantId = thread.TenantId;
            message.UserId = thread.UserId;
            message.ThreadId = thread.Id;
            message.Role = AskMessageRoleEnum.User;
            message.Kind = AskMessageKindEnum.Text;
            message.ContentText = text;
            return await db.AskMessages.CreateAsync(message, false).ConfigureAwait(false);
        }

        private static async Task<Job> CreateJobAsync(DatabaseDriver db, JobKindEnum kind, JobStatusEnum status, DateTime? completedUtc, DateTime? createdUtc = null)
        {
            Job job = new Job();
            job.TenantId = Constants.DefaultTenantId;
            job.UserId = Constants.DefaultUserId;
            job.Name = "retention " + kind;
            job.Kind = kind;
            job.Status = status;
            job.CreatedUtc = createdUtc ?? (completedUtc ?? DateTime.UtcNow).AddMinutes(-5);
            job.CompletedUtc = completedUtc;
            job.LastUpdateUtc = completedUtc ?? job.CreatedUtc;
            return await db.Jobs.CreateAsync(job).ConfigureAwait(false);
        }

        private static async Task<VesselImportBatch> CreateBatchAsync(DatabaseDriver db, VesselImportBatchStatusEnum status, VesselImportCategorizationStatusEnum categorization, DateTime? completedUtc)
        {
            VesselImportBatch batch = new VesselImportBatch();
            batch.TenantId = Constants.DefaultTenantId;
            batch.UserId = Constants.DefaultUserId;
            batch.Status = status;
            batch.CategorizationStatus = categorization;
            batch.CompletedUtc = completedUtc;
            batch.CreatedUtc = DateTime.UtcNow.AddMinutes(-1);
            return await db.VesselImportBatches.CreateAsync(batch).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { TestTags.Positive, TestTags.Database });
        }

        #endregion
    }
}
