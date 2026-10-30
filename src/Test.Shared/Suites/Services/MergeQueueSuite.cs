namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
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
    /// guard, missing-entry handling, and constructor guards. Those cases use a stub git service.
    /// Landed-branch cleanup cases use a real local origin, a real vessel repository, and the git
    /// executable. They assert which mission refs remain after delete.
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

            cases.Add(CaseAsync("delete_landed_none_keeps_local_and_origin_refs", "Delete Landed None Keeps Local And Origin Refs", TestTags.Positive, async () =>
            {
                await AssertLandedBranchCleanupAsync(
                    BranchCleanupPolicyEnum.None,
                    BranchCleanupPolicyEnum.LocalAndRemote,
                    true,
                    true,
                    true);
            }));

            cases.Add(CaseAsync("delete_landed_local_only_keeps_origin_ref", "Delete Landed LocalOnly Keeps Origin Ref", TestTags.Positive, async () =>
            {
                await AssertLandedBranchCleanupAsync(
                    BranchCleanupPolicyEnum.LocalOnly,
                    BranchCleanupPolicyEnum.LocalAndRemote,
                    true,
                    false,
                    true);
            }));

            cases.Add(CaseAsync("delete_landed_inherits_global_none", "Delete Landed Inherits Global None", TestTags.Positive, async () =>
            {
                await AssertLandedBranchCleanupAsync(
                    null,
                    BranchCleanupPolicyEnum.None,
                    true,
                    true,
                    true);
            }));

            cases.Add(CaseAsync("delete_landed_local_and_remote_deletes_both_refs", "Delete Landed LocalAndRemote Deletes Both Refs", TestTags.Positive, async () =>
            {
                await AssertLandedBranchCleanupAsync(
                    BranchCleanupPolicyEnum.LocalAndRemote,
                    BranchCleanupPolicyEnum.None,
                    true,
                    false,
                    false);
            }));

            cases.Add(CaseAsync("delete_landed_unmerged_branch_keeps_refs", "Delete Landed Unmerged Branch Keeps Refs", TestTags.Positive, async () =>
            {
                await AssertLandedBranchCleanupAsync(
                    BranchCleanupPolicyEnum.LocalAndRemote,
                    BranchCleanupPolicyEnum.LocalAndRemote,
                    false,
                    true,
                    true);
            }));

            cases.Add(CaseAsync("delete_cancelled_merged_branch_keeps_current_cleanup", "Delete Cancelled Merged Branch Keeps Current Cleanup", TestTags.Positive, async () =>
            {
                await AssertCancelledMergedBranchCleanupAsync();
            }));

            cases.Add(CaseAsync("process_entry_removes_temporary_integration_worktree", "Process Entry Removes Temporary Integration Worktree", TestTags.Positive, async () =>
            {
                await AssertProcessRemovesIntegrationWorktreeAsync();
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

        private static async Task AssertLandedBranchCleanupAsync(
            BranchCleanupPolicyEnum? vesselPolicy,
            BranchCleanupPolicyEnum settingsPolicy,
            bool containedInTarget,
            bool expectLocalRef,
            bool expectOriginRef)
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                BranchRepository repository = CreateBranchRepository(containedInTarget);
                ArmadaSettings settings = CreateIsolatedSettings();
                settings.BranchCleanupPolicy = settingsPolicy;
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repository, vesselPolicy);
                MergeQueueService queue = CreateGitQueue(testDb.Driver, settings);
                MergeEntry entry = await queue.EnqueueAsync(NewVesselEntry(repository.BranchName, vessel.Id));
                await SetStatusAsync(testDb.Driver, entry, MergeStatusEnum.Landed);

                bool deleted = await queue.DeleteAsync(entry.Id);
                AssertTrue(deleted, "A landed entry is deleted");
                AssertNull(await queue.GetAsync(entry.Id));
                AssertEqual(expectLocalRef, CommitRefExists(repository.VesselPath, "refs/heads/" + repository.BranchName), "local mission ref");
                AssertEqual(expectOriginRef, CommitRefExists(repository.OriginPath, "refs/heads/" + repository.BranchName), "origin mission ref");
            }
        }

        private static async Task AssertCancelledMergedBranchCleanupAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                BranchRepository repository = CreateBranchRepository(true);
                ArmadaSettings settings = CreateIsolatedSettings();
                settings.BranchCleanupPolicy = BranchCleanupPolicyEnum.None;
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repository, BranchCleanupPolicyEnum.None);
                MergeQueueService queue = CreateGitQueue(testDb.Driver, settings);
                MergeEntry entry = await queue.EnqueueAsync(NewVesselEntry(repository.BranchName, vessel.Id));
                await SetStatusAsync(testDb.Driver, entry, MergeStatusEnum.Cancelled);

                bool deleted = await queue.DeleteAsync(entry.Id);
                AssertTrue(deleted, "A cancelled entry is deleted");
                AssertFalse(CommitRefExists(repository.VesselPath, "refs/heads/" + repository.BranchName), "cancelled merged local ref is removed");
                AssertFalse(CommitRefExists(repository.OriginPath, "refs/heads/" + repository.BranchName), "cancelled merged origin ref is removed");
            }
        }

        private static async Task AssertProcessRemovesIntegrationWorktreeAsync()
        {
            using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
            {
                BranchRepository repository = CreateBranchRepository(true);
                ArmadaSettings settings = CreateIsolatedSettings();
                settings.BranchCleanupPolicy = BranchCleanupPolicyEnum.None;
                Vessel vessel = await CreateVesselAsync(testDb.Driver, repository, BranchCleanupPolicyEnum.None);
                MergeQueueService queue = CreateGitQueue(testDb.Driver, settings);
                MergeEntry entry = await queue.EnqueueAsync(NewVesselEntry(repository.BranchName, vessel.Id));

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id);
                AssertNotNull(processed);
                AssertEqual(MergeStatusEnum.Landed, processed!.Status, processed.TestOutput);

                string integrationRoot = Path.Combine(settings.DocksDirectory, "_merge-queue");
                bool worktreeRemains = Directory.Exists(integrationRoot) && Directory.GetDirectories(integrationRoot).Length > 0;
                AssertFalse(worktreeRemains, "temporary integration worktree is removed");
                string listed = RunGitCapture(repository.VesselPath, "worktree", "list", "--porcelain");
                AssertFalse(listed.Contains("_merge-queue", StringComparison.Ordinal), "temporary integration worktree stays registered");
                AssertTrue(CommitRefExists(repository.VesselPath, "refs/heads/" + repository.BranchName), "processing keeps the local mission ref");
                AssertTrue(CommitRefExists(repository.OriginPath, "refs/heads/" + repository.BranchName), "processing keeps the origin mission ref");
            }
        }

        private static MergeEntry NewVesselEntry(string branchName, string vesselId)
        {
            return new MergeEntry
            {
                BranchName = branchName,
                TargetBranch = "main",
                VesselId = vesselId
            };
        }

        private static async Task<Vessel> CreateVesselAsync(DatabaseDriver db, BranchRepository repository, BranchCleanupPolicyEnum? policy)
        {
            Vessel vessel = new Vessel("example-vessel", repository.OriginPath);
            vessel.LocalPath = repository.VesselPath;
            vessel.DefaultBranch = "main";
            vessel.BranchCleanupPolicy = policy;
            return await db.Vessels.CreateAsync(vessel);
        }

        private static MergeQueueService CreateGitQueue(DatabaseDriver db, ArmadaSettings settings)
        {
            return new MergeQueueService(CreateLogging(), db, settings, new GitService(CreateLogging()));
        }

        private static ArmadaSettings CreateIsolatedSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("mqdocks");
            settings.ReposDirectory = TestTemp.NewDirectory("mqrepos");
            return settings;
        }

        private static BranchRepository CreateBranchRepository(bool containedInTarget)
        {
            string root = TestTemp.NewDirectory("mqpolicy");
            string work = Path.Combine(root, "work");
            string origin = Path.Combine(root, "origin.git");
            string vessel = Path.Combine(root, "vessel.git");
            Directory.CreateDirectory(work);
            RunGit(work, "init", "-b", "main");
            RunGit(work, "config", "user.name", "Armada Tests");
            RunGit(work, "config", "user.email", "armada-tests@example.com");
            File.WriteAllText(Path.Combine(work, "README.md"), "hello\n");
            RunGit(work, "add", "README.md");
            RunGit(work, "commit", "-m", "Initial commit");
            RunGit(work, "branch", "topic");
            if (!containedInTarget)
            {
                RunGit(work, "checkout", "topic");
                File.WriteAllText(Path.Combine(work, "MORE.md"), "more\n");
                RunGit(work, "add", "MORE.md");
                RunGit(work, "commit", "-m", "Topic commit");
            }

            RunGit(root, "clone", "--bare", work, origin);
            RunGit(root, "clone", "--bare", origin, vessel);
            RunGit(vessel, "config", "user.name", "Armada Tests");
            RunGit(vessel, "config", "user.email", "armada-tests@example.com");
            return new BranchRepository(origin, vessel, "topic");
        }

        private static bool CommitRefExists(string repositoryPath, string gitRef)
        {
            GitProcessResult result = RunGitResult(repositoryPath, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}");
            return result.ExitCode == 0;
        }

        private static string RunGitCapture(string workingDirectory, params string[] arguments)
        {
            GitProcessResult result = RunGitResult(workingDirectory, arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "git " + String.Join(" ", arguments) + " failed (exit " + result.ExitCode + "): " + result.StandardError.Trim());
            }

            return result.StandardOutput;
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
        {
            GitProcessResult result = RunGitResult(workingDirectory, arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "git " + String.Join(" ", arguments) + " failed (exit " + result.ExitCode + "): " + result.StandardError.Trim());
            }
        }

        private static GitProcessResult RunGitResult(string workingDirectory, params string[] arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using (Process process = new Process { StartInfo = startInfo })
            {
                try
                {
                    process.Start();
                }
                catch (Win32Exception ex)
                {
                    throw new InvalidOperationException("git is not installed.", ex);
                }

                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new GitProcessResult(process.ExitCode, standardOutput, standardError);
            }
        }

        private sealed class BranchRepository
        {
            /// <summary>
            /// A local origin and the vessel bare clone that tracks it.
            /// </summary>
            /// <param name="originPath">Bare origin repository.</param>
            /// <param name="vesselPath">Bare vessel repository.</param>
            /// <param name="branchName">Mission branch name.</param>
            public BranchRepository(string originPath, string vesselPath, string branchName)
            {
                OriginPath = originPath;
                VesselPath = vesselPath;
                BranchName = branchName;
            }

            /// <summary>
            /// Bare origin repository.
            /// </summary>
            public string OriginPath { get; }

            /// <summary>
            /// Bare vessel repository.
            /// </summary>
            public string VesselPath { get; }

            /// <summary>
            /// Mission branch name.
            /// </summary>
            public string BranchName { get; }
        }

        private sealed class GitProcessResult
        {
            /// <summary>
            /// Result of one git process.
            /// </summary>
            /// <param name="exitCode">Process exit code.</param>
            /// <param name="standardOutput">Standard output.</param>
            /// <param name="standardError">Standard error.</param>
            public GitProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            /// <summary>
            /// Process exit code.
            /// </summary>
            public int ExitCode { get; }

            /// <summary>
            /// Standard output.
            /// </summary>
            public string StandardOutput { get; }

            /// <summary>
            /// Standard error.
            /// </summary>
            public string StandardError { get; }
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
