namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MergeQueueService"/>: enqueue/read/cancel plus the terminal-only
    /// delete and bulk purge added for the reliability release. Positive cases assert persistence and
    /// that terminal entries are deletable/purgeable; negative cases assert the non-terminal delete
    /// guard, missing-entry handling, and constructor guards. A stub git service stands in for real
    /// branch cleanup so the queue logic is exercised without a repository.
    /// </summary>
    public sealed class MergeQueueSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MergeQueue";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Merge Queue suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_git_throws", "MergeQueueService NullGit Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new MergeQueueService(CreateLogging(), null!, CreateSettings(), null!));
            }));

            cases.Add(CaseAsync("enqueue_persists_as_queued", "Enqueue PersistsAsQueued", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/a"));

                    MergeEntry? read = await queue.GetAsync(entry.Id);
                    AssertNotNull(read);
                    AssertEqual(MergeStatusEnum.Queued, read!.Status);
                    AssertEqual("feature/a", read.BranchName);
                }
            }));

            cases.Add(CaseAsync("get_nonexistent_returns_null", "Get Nonexistent ReturnsNull", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    AssertNull(await queue.GetAsync("mrg_does_not_exist"));
                }
            }));

            cases.Add(CaseAsync("cancel_sets_cancelled", "Cancel SetsCancelled", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/cancel"));

                    await queue.CancelAsync(entry.Id);
                    MergeEntry? read = await queue.GetAsync(entry.Id);
                    AssertNotNull(read);
                    AssertEqual(MergeStatusEnum.Cancelled, read!.Status);
                }
            }));

            cases.Add(CaseAsync("cancel_landed_keeps_status", "Cancel Landed KeepsStatus", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MergeQueueService queue = CreateQueue(db);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/landed"));
                    await SetStatusAsync(db, entry, MergeStatusEnum.Landed);

                    await queue.CancelAsync(entry.Id);
                    MergeEntry? read = await queue.GetAsync(entry.Id);
                    AssertNotNull(read);
                    AssertEqual(MergeStatusEnum.Landed, read!.Status);
                }
            }));

            cases.Add(CaseAsync("cancel_failed_keeps_status", "Cancel Failed KeepsStatus", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MergeQueueService queue = CreateQueue(db);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/failed"));
                    await SetStatusAsync(db, entry, MergeStatusEnum.Failed);

                    await queue.CancelAsync(entry.Id);
                    MergeEntry? read = await queue.GetAsync(entry.Id);
                    AssertNotNull(read);
                    AssertEqual(MergeStatusEnum.Failed, read!.Status);
                }
            }));

            cases.Add(CaseAsync("delete_terminal_entry_succeeds", "Delete TerminalEntry Succeeds", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MergeQueueService queue = CreateQueue(db);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/done"));
                    await SetStatusAsync(db, entry, MergeStatusEnum.Failed);

                    bool deleted = await queue.DeleteAsync(entry.Id);
                    AssertTrue(deleted, "Terminal entry should delete");
                    AssertNull(await queue.GetAsync(entry.Id));
                }
            }));

            cases.Add(CaseAsync("delete_non_terminal_returns_false", "Delete NonTerminal ReturnsFalse", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await queue.EnqueueAsync(NewEntry("feature/queued"));

                    bool deleted = await queue.DeleteAsync(entry.Id);
                    AssertFalse(deleted, "Queued (non-terminal) entry must not delete");
                    AssertNotNull(await queue.GetAsync(entry.Id));
                }
            }));

            cases.Add(CaseAsync("delete_nonexistent_returns_false", "Delete Nonexistent ReturnsFalse", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    AssertFalse(await queue.DeleteAsync("mrg_missing"));
                }
            }));

            cases.Add(CaseAsync("purge_removes_only_terminal", "Purge RemovesOnlyTerminal", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MergeQueueService queue = CreateQueue(db);
                    MergeEntry queued = await queue.EnqueueAsync(NewEntry("feature/keep"));
                    MergeEntry landed = await queue.EnqueueAsync(NewEntry("feature/landed"));
                    MergeEntry failed = await queue.EnqueueAsync(NewEntry("feature/failed"));
                    await SetStatusAsync(db, landed, MergeStatusEnum.Landed);
                    await SetStatusAsync(db, failed, MergeStatusEnum.Failed);

                    int purged = await queue.PurgeTerminalAsync();
                    AssertEqual(2, purged);
                    AssertNotNull(await queue.GetAsync(queued.Id));
                    AssertNull(await queue.GetAsync(landed.Id));
                    AssertNull(await queue.GetAsync(failed.Id));
                }
            }));

            cases.Add(CaseAsync("purge_with_status_filter", "Purge WithStatusFilter", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    MergeQueueService queue = CreateQueue(db);
                    MergeEntry landed = await queue.EnqueueAsync(NewEntry("feature/l"));
                    MergeEntry failed = await queue.EnqueueAsync(NewEntry("feature/f"));
                    await SetStatusAsync(db, landed, MergeStatusEnum.Landed);
                    await SetStatusAsync(db, failed, MergeStatusEnum.Failed);

                    int purged = await queue.PurgeTerminalAsync(status: MergeStatusEnum.Failed);
                    AssertEqual(1, purged);
                    AssertNotNull(await queue.GetAsync(landed.Id));
                    AssertNull(await queue.GetAsync(failed.Id));
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Merge Queue",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MergeEntry NewEntry(string branch)
        {
            return new MergeEntry
            {
                BranchName = branch,
                TargetBranch = "main",
                MissionId = "msn_test"
            };
        }

        private static async Task SetStatusAsync(DatabaseDriver db, MergeEntry entry, MergeStatusEnum status)
        {
            entry.Status = status;
            await db.MergeEntries.UpdateAsync(entry);
        }

        private static MergeQueueService CreateQueue(DatabaseDriver db)
        {
            return new MergeQueueService(CreateLogging(), db, CreateSettings(), new StubGitService());
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
