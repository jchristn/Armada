namespace Armada.Test.Unit.Suites.Services
{
    using System.Diagnostics;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class DockServiceTests : TestSuite
    {
        public override string Name => "Dock Service";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ProvisionAsync serializes repo worktree creation per vessel repo", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    ArmadaSettings settings = new ArmadaSettings();
                    settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
                    settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));

                    LockingGitService git = new LockingGitService();
                    DockService service = new DockService(logging, testDb.Driver, settings, git);

                    Vessel vessel = new Vessel("test-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(settings.ReposDirectory, vessel.Name + ".git");
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_workdir_" + Guid.NewGuid().ToString("N"));
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain1 = new Captain("captain-1");
                    Captain captain2 = new Captain("captain-2");
                    captain1 = await testDb.Driver.Captains.CreateAsync(captain1).ConfigureAwait(false);
                    captain2 = await testDb.Driver.Captains.CreateAsync(captain2).ConfigureAwait(false);

                    Task<Dock?> first = service.ProvisionAsync(vessel, captain1, "armada/captain-1/msn_one", "msn_one");
                    Task<Dock?> second = service.ProvisionAsync(vessel, captain2, "armada/captain-2/msn_two", "msn_two");

                    Dock?[] docks = await Task.WhenAll(first, second).ConfigureAwait(false);

                    AssertNotNull(docks[0], "First dock should be provisioned");
                    AssertNotNull(docks[1], "Second dock should be provisioned");
                    AssertEqual(1, git.MaxConcurrentCreateCalls, "Concurrent worktree creation against the same repo should be serialized");
                }
            });

            await RunTest("ProvisionAsync missing configured default branch reuses repo history instead of seeding orphan repo", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    ArmadaSettings settings = new ArmadaSettings();
                    settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
                    settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));

                    GitService git = new GitService(logging);
                    DockService service = new DockService(logging, testDb.Driver, settings, git);

                    string rootDir = Path.Combine(Path.GetTempPath(), "armada-dockservice-" + Guid.NewGuid().ToString("N"));
                    string sourceDir = Path.Combine(rootDir, "source");
                    string workDir = Path.Combine(rootDir, "target");

                    try
                    {
                        Directory.CreateDirectory(sourceDir);
                        await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                        await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                        await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                        await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                        await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                        string sourceHead = (await RunGitAsync(sourceDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                        Vessel vessel = new Vessel("test-vessel", sourceDir);
                        vessel.DefaultBranch = "release/e2e";
                        vessel.WorkingDirectory = workDir;
                        vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                        Captain captain = new Captain("captain-1");
                        captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                        Dock? dock = await service.ProvisionAsync(vessel, captain, "armada/captain-1/msn_one", "msn_one").ConfigureAwait(false);
                        AssertNotNull(dock, "Dock should be provisioned");

                        Vessel? reloadedVessel = await testDb.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                        AssertNotNull(reloadedVessel, "Vessel should remain readable");
                        AssertFalse(String.IsNullOrEmpty(reloadedVessel!.LocalPath), "Provisioning should populate the bare repo path");

                        string repoPath = reloadedVessel.LocalPath!;
                        string defaultBranchCommit = (await RunGitAsync(repoPath, "rev-parse", "refs/heads/release/e2e").ConfigureAwait(false)).Trim();
                        string worktreeHead = (await RunGitAsync(dock!.WorktreePath!, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                        AssertEqual(sourceHead, defaultBranchCommit, "Configured default branch should be created from the existing repo history");
                        AssertEqual(sourceHead, worktreeHead, "Provisioned worktree should start from the source repo history");
                    }
                    finally
                    {
                        if (Directory.Exists(rootDir))
                        {
                            try { Directory.Delete(rootDir, true); }
                            catch { }
                        }

                        if (Directory.Exists(settings.DocksDirectory))
                        {
                            try { Directory.Delete(settings.DocksDirectory, true); }
                            catch { }
                        }

                        if (Directory.Exists(settings.ReposDirectory))
                        {
                            try { Directory.Delete(settings.ReposDirectory, true); }
                            catch { }
                        }
                    }
                }
            });

            await RunTest("ProvisionAsync fresh clone skips redundant fetch before first dock", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;

                    ArmadaSettings settings = new ArmadaSettings();
                    settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
                    settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));

                    FreshCloneGitService git = new FreshCloneGitService();
                    DockService service = new DockService(logging, testDb.Driver, settings, git);

                    try
                    {
                        Vessel vessel = new Vessel("test-vessel", "https://github.com/test/repo.git");
                        vessel.LocalPath = Path.Combine(settings.ReposDirectory, vessel.Name + ".git");
                        vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_workdir_" + Guid.NewGuid().ToString("N"));
                        vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                        Captain captain = new Captain("captain-1");
                        captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                        Dock? dock = await service.ProvisionAsync(vessel, captain, "armada/captain-1/msn_one", "msn_one").ConfigureAwait(false);

                        AssertNotNull(dock, "Dock should be provisioned on a fresh clone");
                        AssertEqual(0, git.FetchCalls, "Freshly cloned repos should not immediately refetch before first dock creation");
                        AssertEqual(1, git.CloneCalls, "Provisioning should clone the bare repo exactly once");
                    }
                    finally
                    {
                        if (Directory.Exists(settings.DocksDirectory))
                        {
                            try { Directory.Delete(settings.DocksDirectory, true); }
                            catch { }
                        }

                        if (Directory.Exists(settings.ReposDirectory))
                        {
                            try { Directory.Delete(settings.ReposDirectory, true); }
                            catch { }
                        }
                    }
                }
            });
        }

        private static async Task<string> RunGitAsync(string workingDirectory, params string[] args)
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

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using Process process = new Process { StartInfo = startInfo };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("git " + String.Join(" ", args) + " failed (exit " + process.ExitCode + "): " + stderr.Trim());
            }

            return stdout;
        }

        private class LockingGitService : IGitService
        {
            private int _CurrentCreateCalls;
            public int MaxConcurrentCreateCalls { get; private set; }

            public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
            {
                Directory.CreateDirectory(localPath);
                return Task.CompletedTask;
            }

            public async Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
            {
                int current = Interlocked.Increment(ref _CurrentCreateCalls);
                if (current > MaxConcurrentCreateCalls)
                    MaxConcurrentCreateCalls = current;

                try
                {
                    Directory.CreateDirectory(worktreePath);
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _CurrentCreateCalls);
                }
            }

            public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
            public Task FetchAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
            public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;
            public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default) => Task.FromResult(String.Empty);
            public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
            public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(Directory.Exists(path));
            public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;
            public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;
            public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
            public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;
            public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default) => Task.CompletedTask;
            public Task PullAsync(string workingDirectory, CancellationToken token = default) => Task.CompletedTask;
            public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) => Task.FromResult(String.Empty);
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123");
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(false);
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) => Task.FromResult(true);
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default, bool skipFetch = false) => Task.FromResult(true);
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        }

        private class FreshCloneGitService : IGitService
        {
            private bool _RepoExists;

            public int CloneCalls { get; private set; }
            public int FetchCalls { get; private set; }

            public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
            {
                CloneCalls++;
                _RepoExists = true;
                Directory.CreateDirectory(localPath);
                return Task.CompletedTask;
            }

            public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
            {
                Directory.CreateDirectory(worktreePath);
                return Task.CompletedTask;
            }

            public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;

            public Task FetchAsync(string repoPath, CancellationToken token = default)
            {
                FetchCalls++;
                return Task.CompletedTask;
            }

            public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;
            public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default) => Task.FromResult(String.Empty);
            public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;
            public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(_RepoExists);
            public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;
            public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;
            public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
            public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;
            public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default) => Task.CompletedTask;
            public Task PullAsync(string workingDirectory, CancellationToken token = default) => Task.CompletedTask;
            public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) => Task.FromResult(String.Empty);
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123");
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(false);
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) => Task.FromResult(true);
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default, bool skipFetch = false) => Task.FromResult(true);
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        }
    }
}
