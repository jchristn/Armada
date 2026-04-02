namespace Armada.Test.Unit.Suites.Services
{
    using System.Diagnostics;
    using Armada.Core.Services;
    using Armada.Test.Common;
    using SyslogLogging;

    public class GitServiceTests : TestSuite
    {
        public override string Name => "Git Service";

        private GitService CreateService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new GitService(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Constructor NullLogging Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() => new GitService(null!));
            });

            await RunTest("CloneBareAsync NullRepoUrl Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync(null!, "/tmp/path"));
            });

            await RunTest("CloneBareAsync EmptyRepoUrl Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("", "/tmp/path"));
            });

            await RunTest("CloneBareAsync NullLocalPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("https://github.com/test/repo", null!));
            });

            await RunTest("CreateWorktreeAsync NullRepoPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync(null!, "/tmp/wt", "branch"));
            });

            await RunTest("CreateWorktreeAsync NullWorktreePath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", null!, "branch"));
            });

            await RunTest("CreateWorktreeAsync NullBranchName Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", "/tmp/wt", null!));
            });

            await RunTest("RemoveWorktreeAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RemoveWorktreeAsync(null!));
            });

            await RunTest("RemoveWorktreeAsync Removes Registered Worktree From Outside Repo", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await service.CreateWorktreeAsync(bareDir, worktreeDir, "armada/remove-me", "main").ConfigureAwait(false);

                    bool before = await service.IsWorktreeRegisteredAsync(bareDir, worktreeDir).ConfigureAwait(false);
                    AssertTrue(before, "Worktree should be registered before removal");

                    await service.RemoveWorktreeAsync(worktreeDir).ConfigureAwait(false);

                    AssertFalse(Directory.Exists(worktreeDir), "Worktree directory should be removed");

                    bool after = await service.IsWorktreeRegisteredAsync(bareDir, worktreeDir).ConfigureAwait(false);
                    AssertFalse(after, "Worktree should no longer be registered after removal");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("FetchAsync NullRepoPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.FetchAsync(null!));
            });

            await RunTest("PushBranchAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.PushBranchAsync(null!));
            });

            await RunTest("CreatePullRequestAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync(null!, "title", "body"));
            });

            await RunTest("CreatePullRequestAsync NullTitle Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync("/tmp/wt", null!, "body"));
            });

            await RunTest("RepairWorktreeAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RepairWorktreeAsync(null!));
            });

            await RunTest("IsRepositoryAsync NullPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync(null!);
                AssertFalse(result);
            });

            await RunTest("IsRepositoryAsync EmptyPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("");
                AssertFalse(result);
            });

            await RunTest("IsRepositoryAsync NonExistentPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("/tmp/nonexistent_" + Guid.NewGuid().ToString("N"));
                AssertFalse(result);
            });

            await RunTest("CreateWorktreeAsync NewBranch StartsAtBaseCommit", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    string baseCommit = (await RunGitAsync(sourceDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await service.CreateWorktreeAsync(bareDir, worktreeDir, "armada/test-branch", "main").ConfigureAwait(false);

                    string worktreeHead = (await RunGitAsync(worktreeDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    AssertEqual(baseCommit, worktreeHead);

                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("CreateWorktreeAsync NewBranch UsesLatestRemoteBaseCommit", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await service.CloneBareAsync(sourceDir, bareDir).ConfigureAwait(false);

                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\nlatest base\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Advance main").ConfigureAwait(false);
                    string latestBaseCommit = (await RunGitAsync(sourceDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    await service.CreateWorktreeAsync(bareDir, worktreeDir, "armada/latest-base", "main").ConfigureAwait(false);

                    string worktreeHead = (await RunGitAsync(worktreeDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    AssertEqual(latestBaseCommit, worktreeHead, "New worktree should start from the latest fetched base branch commit");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("EnsureLocalBranchAsync MissingBranch UsesExistingRepoHistory", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");

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

                    await service.CloneBareAsync(sourceDir, bareDir).ConfigureAwait(false);

                    bool ensured = await service.EnsureLocalBranchAsync(bareDir, "release/e2e", skipFetch: true).ConfigureAwait(false);
                    string ensuredCommit = (await RunGitAsync(bareDir, "rev-parse", "refs/heads/release/e2e").ConfigureAwait(false)).Trim();

                    AssertTrue(ensured, "EnsureLocalBranchAsync should create a missing branch when repo history exists");
                    AssertEqual(sourceHead, ensuredCommit, "Created branch should point at the existing default history");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("EnsureLocalBranchAsync ExistingRemoteTrackingBranch DoesNotRequireFetch", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);

                    string sourceHead = (await RunGitAsync(bareDir, "rev-parse", "refs/heads/main").ConfigureAwait(false)).Trim();
                    await RunGitAsync(bareDir, "update-ref", "refs/remotes/origin/release/e2e", sourceHead).ConfigureAwait(false);
                    await RunGitAsync(bareDir, "remote", "set-url", "origin", Path.Combine(rootDir, "missing-remote.git")).ConfigureAwait(false);

                    bool ensured = await service.EnsureLocalBranchAsync(bareDir, "release/e2e", skipFetch: true).ConfigureAwait(false);
                    string ensuredCommit = (await RunGitAsync(bareDir, "rev-parse", "refs/heads/release/e2e").ConfigureAwait(false)).Trim();

                    AssertTrue(ensured, "EnsureLocalBranchAsync should create the branch from existing remote-tracking refs");
                    AssertEqual(sourceHead, ensuredCommit, "Created branch should use the existing remote-tracking commit without fetching");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("CreateWorktreeAsync ExistingBranch StaysOnNamedBranch", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(bareDir, "branch", "armada/existing", "main").ConfigureAwait(false);

                    await service.CreateWorktreeAsync(bareDir, worktreeDir, "armada/existing", "main").ConfigureAwait(false);

                    string currentBranch = (await RunGitAsync(worktreeDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    AssertEqual("armada/existing", currentBranch);
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("CreateWorktreeAsync DirtyTrackedFiles Throws And Cleans Up", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");
                string branchName = "armada/dirty-worktree";

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    Directory.CreateDirectory(Path.Combine(sourceDir, "test"));
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);

                    await File.WriteAllTextAsync(
                        Path.Combine(sourceDir, "test", "Dirty.csproj"),
                        "<Project>\r\n  <PropertyGroup />\r\n</Project>\r\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "test/Dirty.csproj").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Add project before attributes").ConfigureAwait(false);

                    await File.WriteAllTextAsync(
                        Path.Combine(sourceDir, ".gitattributes"),
                        "*.csproj text eol=crlf\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", ".gitattributes").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Add gitattributes without renormalizing").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);

                    InvalidOperationException? ex = null;
                    try
                    {
                        await service.CreateWorktreeAsync(bareDir, worktreeDir, branchName, "main").ConfigureAwait(false);
                        throw new Exception("Assertion failed: expected InvalidOperationException but no exception was thrown");
                    }
                    catch (InvalidOperationException caught)
                    {
                        ex = caught;
                    }

                    AssertTrue(ex != null, "Expected dirty worktree creation to throw");
                    AssertTrue(ex!.Message.Contains("contains tracked modifications", StringComparison.Ordinal), "Exception should explain that the checkout is dirty");
                    AssertTrue(ex.Message.Contains("test/Dirty.csproj", StringComparison.Ordinal), "Exception should list the dirty tracked file");
                    AssertFalse(Directory.Exists(worktreeDir), "Failed worktree creation should clean up the worktree directory");

                    string branchList = await RunGitAsync(bareDir, "branch", "--list", branchName).ConfigureAwait(false);
                    AssertEqual(String.Empty, branchList.Trim(), "Failed worktree creation should delete the created branch ref");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("FetchAsync CheckedOutWorktreeBranch UsesRemoteTrackingRefs", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await service.CloneBareAsync(sourceDir, bareDir).ConfigureAwait(false);
                    await service.CreateWorktreeAsync(bareDir, worktreeDir, "armada/feature", "main").ConfigureAwait(false);

                    string originalLocalBranchCommit = (await RunGitAsync(bareDir, "rev-parse", "refs/heads/armada/feature").ConfigureAwait(false)).Trim();

                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/feature").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "hello\nremote feature change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Advance remote feature").ConfigureAwait(false);
                    string remoteFeatureCommit = (await RunGitAsync(sourceDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    await service.FetchAsync(bareDir).ConfigureAwait(false);

                    string trackedRemoteCommit = (await RunGitAsync(bareDir, "rev-parse", "refs/remotes/origin/armada/feature").ConfigureAwait(false)).Trim();
                    string localBranchCommit = (await RunGitAsync(bareDir, "rev-parse", "refs/heads/armada/feature").ConfigureAwait(false)).Trim();
                    string checkedOutBranch = (await RunGitAsync(worktreeDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();

                    AssertEqual(remoteFeatureCommit, trackedRemoteCommit, "Fetch should update the remote-tracking ref for the checked-out branch");
                    AssertEqual("armada/feature", checkedOutBranch, "Fetch should not disturb the active worktree branch");
                    AssertEqual(originalLocalBranchCommit, localBranchCommit, "Fetch should not rewrite the checked-out local branch ref");
                    AssertNotEqual(remoteFeatureCommit, localBranchCommit, "The checked-out local branch should remain untouched when the remote advances");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("DiffAsync NoMergeBase FallsBackToTwoDotDiff", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));

                try
                {
                    Directory.CreateDirectory(rootDir);
                    await RunGitAsync(rootDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(rootDir, "README.md"), "hello\n").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "checkout", "--orphan", "armada/orphan").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "rm", "-rf", ".").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(rootDir, "README.md"), "hello\norphan change\n").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "commit", "-m", "Orphan commit").ConfigureAwait(false);

                    string diff = await service.DiffAsync(rootDir, "main").ConfigureAwait(false);

                    AssertTrue(diff.Contains("README.md", StringComparison.Ordinal), "Diff should include the changed file");
                    AssertTrue(diff.Contains("orphan change", StringComparison.Ordinal), "Diff should include the orphan-branch change");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("MergeBranchLocalAsync Cleans Conflict State After Failure", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/conflict").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "branch change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Branch change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/conflict").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, "README.md"), "target change\n").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "commit", "-am", "Target change").ConfigureAwait(false);

                    await AssertThrowsAsync<InvalidOperationException>(() =>
                        service.MergeBranchLocalAsync(targetDir, bareDir, "armada/conflict", "main"));

                    string status = (await RunGitAsync(targetDir, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false)).Trim();
                    string currentBranch = (await RunGitAsync(targetDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    string fileContents = await File.ReadAllTextAsync(Path.Combine(targetDir, "README.md")).ConfigureAwait(false);

                    AssertEqual(String.Empty, status, "Conflict cleanup should leave no staged or unmerged changes");
                    AssertEqual("main", currentBranch, "Conflict cleanup should return to the target branch");
                    AssertEqual("target change\n", fileContents, "Conflict cleanup should restore the pre-merge working tree");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("MergeBranchLocalAsync Materializes MissingTargetBranch In Landing Checkout", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);

                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada-v050-live").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "target-only.txt"), "target branch content\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "target-only.txt").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Create target branch").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada-v050-live").ConfigureAwait(false);

                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/worker-1").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base\nworker change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Worker change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/worker-1").ConfigureAwait(false);

                    string missingLocalBranch = (await RunGitAsync(targetDir, "branch", "--list", "armada-v050-live").ConfigureAwait(false)).Trim();
                    AssertEqual(String.Empty, missingLocalBranch, "Landing checkout should not already have the target branch locally");

                    await service.MergeBranchLocalAsync(targetDir, bareDir, "armada/worker-1", "armada-v050-live").ConfigureAwait(false);

                    string currentBranch = (await RunGitAsync(targetDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    string localBranch = (await RunGitAsync(targetDir, "branch", "--list", "armada-v050-live").ConfigureAwait(false)).Trim();
                    string mergedReadme = await File.ReadAllTextAsync(Path.Combine(targetDir, "README.md")).ConfigureAwait(false);
                    string targetBranchFile = await File.ReadAllTextAsync(Path.Combine(targetDir, "target-only.txt")).ConfigureAwait(false);

                    AssertEqual("armada-v050-live", currentBranch, "Landing checkout should end on the materialized target branch");
                    AssertTrue(!String.IsNullOrWhiteSpace(localBranch), "Landing checkout should create a local target branch when it is missing");
                    AssertEqual("base\nworker change\n", mergedReadme, "Landing merge should include worker changes");
                    AssertEqual("target branch content\n", targetBranchFile, "Landing merge should preserve target branch files");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            });

            await RunTest("MergeBranchLocalAsync DirtyLandingCheckout Throws Before Merge", async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    Directory.CreateDirectory(sourceDir);
                    await RunGitAsync(sourceDir, "init", "-b", "main").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "README.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Initial commit").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);

                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/worker-2").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base\nworker change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Worker change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/worker-2").ConfigureAwait(false);

                    await File.WriteAllTextAsync(Path.Combine(targetDir, "README.md"), "dirty landing checkout\n").ConfigureAwait(false);

                    InvalidOperationException? ex = null;
                    try
                    {
                        await service.MergeBranchLocalAsync(targetDir, bareDir, "armada/worker-2", "main").ConfigureAwait(false);
                    }
                    catch (InvalidOperationException caught)
                    {
                        ex = caught;
                    }

                    string currentBranch = (await RunGitAsync(targetDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    string fileContents = await File.ReadAllTextAsync(Path.Combine(targetDir, "README.md")).ConfigureAwait(false);

                    AssertNotNull(ex, "Dirty landing checkout should throw");
                    AssertTrue(ex.Message.Contains("contains tracked modifications", StringComparison.Ordinal), "Dirty landing checkout should be rejected with a clear error");
                    AssertEqual("main", currentBranch, "Dirty landing checkout should not switch branches");
                    AssertEqual("dirty landing checkout\n", fileContents, "Dirty landing checkout should remain untouched");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
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
                throw new InvalidOperationException("git failed (exit " + process.ExitCode + "): " + stderr.Trim());
            }

            return stdout;
        }
    }
}
