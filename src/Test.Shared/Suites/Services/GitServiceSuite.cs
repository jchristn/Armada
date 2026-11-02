namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="GitService"/>: argument validation and real git-process behavior
    /// for bare clone, worktree lifecycle, fetch, diff, and local landing merges. Cases that shell
    /// out to git create isolated temp repositories under the system temp path and clean up after
    /// themselves. Negative cases cover null/empty arguments, dirty checkouts, and merge conflicts.
    /// </summary>
    public sealed class GitServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Git Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_logging_throws", "Constructor NullLogging Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => new GitService(null!));
            }));

            cases.Add(CaseAsync("clone_bare_async_null_repo_url_throws", "CloneBareAsync NullRepoUrl Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync(null!, "/tmp/path"));
            }));

            cases.Add(CaseAsync("clone_bare_async_empty_repo_url_throws", "CloneBareAsync EmptyRepoUrl Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("", "/tmp/path"));
            }));

            cases.Add(CaseAsync("clone_bare_async_null_local_path_throws", "CloneBareAsync NullLocalPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("https://github.com/test/repo", null!));
            }));

            cases.Add(CaseAsync("create_worktree_async_null_repo_path_throws", "CreateWorktreeAsync NullRepoPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync(null!, "/tmp/wt", "branch"));
            }));

            cases.Add(CaseAsync("create_worktree_async_null_worktree_path_throws", "CreateWorktreeAsync NullWorktreePath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", null!, "branch"));
            }));

            cases.Add(CaseAsync("create_worktree_async_null_branch_name_throws", "CreateWorktreeAsync NullBranchName Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", "/tmp/wt", null!));
            }));

            cases.Add(CaseAsync("remove_worktree_async_null_path_throws", "RemoveWorktreeAsync NullPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RemoveWorktreeAsync(null!));
            }));

            cases.Add(CaseAsync("remove_worktree_async_removes_registered_worktree_from_outside_repo", "RemoveWorktreeAsync Removes Registered Worktree From Outside Repo", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
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
            }));

            cases.Add(CaseAsync("fetch_async_null_repo_path_throws", "FetchAsync NullRepoPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.FetchAsync(null!));
            }));

            cases.Add(CaseAsync("push_branch_async_null_path_throws", "PushBranchAsync NullPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.PushBranchAsync(null!));
            }));

            cases.Add(CaseAsync("create_pull_request_async_null_path_throws", "CreatePullRequestAsync NullPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync(null!, "title", "body"));
            }));

            cases.Add(CaseAsync("create_pull_request_async_null_title_throws", "CreatePullRequestAsync NullTitle Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync("/tmp/wt", null!, "body"));
            }));

            cases.Add(CaseAsync("repair_worktree_async_null_path_throws", "RepairWorktreeAsync NullPath Throws", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RepairWorktreeAsync(null!));
            }));

            cases.Add(CaseAsync("is_repository_async_null_path_returns_false", "IsRepositoryAsync NullPath ReturnsFalse", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync(null!);
                AssertFalse(result);
            }));

            cases.Add(CaseAsync("is_repository_async_empty_path_returns_false", "IsRepositoryAsync EmptyPath ReturnsFalse", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("");
                AssertFalse(result);
            }));

            cases.Add(CaseAsync("is_repository_async_non_existent_path_returns_false", "IsRepositoryAsync NonExistentPath ReturnsFalse", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("/tmp/nonexistent_" + Guid.NewGuid().ToString("N"));
                AssertFalse(result);
            }));

            cases.Add(CaseAsync("create_worktree_async_new_branch_starts_at_base_commit", "CreateWorktreeAsync NewBranch StartsAtBaseCommit", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
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
            }));

            cases.Add(CaseAsync("create_worktree_async_new_branch_uses_latest_remote_base_commit", "CreateWorktreeAsync NewBranch UsesLatestRemoteBaseCommit", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
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
            }));

            cases.Add(CaseAsync("ensure_local_branch_async_missing_branch_uses_existing_repo_history", "EnsureLocalBranchAsync MissingBranch UsesExistingRepoHistory", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");

                try
                {
                    string sourceHead = (await RunGitAsync(sourceDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    await service.CloneBareAsync(sourceDir, bareDir).ConfigureAwait(false);

                    bool ensured = await service.EnsureLocalBranchAsync(bareDir, "release/e2e").ConfigureAwait(false);
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
            }));

            cases.Add(CaseAsync("create_worktree_async_existing_branch_stays_on_named_branch", "CreateWorktreeAsync ExistingBranch StaysOnNamedBranch", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
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
            }));

            cases.Add(CaseAsync("create_worktree_async_dirty_tracked_files_throws_and_cleans_up", "CreateWorktreeAsync DirtyTrackedFiles Throws And Cleans Up", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string rootDir = Path.Combine(Path.GetTempPath(), "armada-gitservice-" + Guid.NewGuid().ToString("N"));
                string sourceDir = Path.Combine(rootDir, "source");
                string bareDir = Path.Combine(rootDir, "bare.git");
                string hooksDir = Path.Combine(rootDir, "hooks");
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
                        "<Project>\n  <PropertyGroup />\n</Project>\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "test/Dirty.csproj").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Add tracked file").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    Directory.CreateDirectory(hooksDir);

                    // Dirty the tracked checkout deterministically during `git worktree add`
                    // without depending on line-ending behavior in the host Git install.
                    await File.WriteAllTextAsync(
                        Path.Combine(hooksDir, "post-checkout"),
                        "#!/bin/sh\nprintf '\\n<!-- dirty -->\\n' >> test/Dirty.csproj\n").ConfigureAwait(false);
                    // git only runs executable hooks on Linux/macOS.
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(Path.Combine(hooksDir, "post-checkout"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    await RunGitAsync(bareDir, "config", "core.hooksPath", hooksDir).ConfigureAwait(false);

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
                    AssertFalse(ex is GitCommandException, "the dirty checkout is rejected by Armada's check, not by a failed git command");
                    GitDirtyCheckoutException? dirty = ex as GitDirtyCheckoutException;
                    AssertNotNull(dirty, "a dirty checkout is reported as GitDirtyCheckoutException, got " + ex!.GetType().Name);
                    AssertTrue(dirty!.ModifiedPaths.Contains("test/Dirty.csproj"), "the exception lists the dirty tracked file: " + String.Join(", ", dirty.ModifiedPaths));
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
            }));

            cases.Add(CaseAsync("fetch_async_checked_out_worktree_branch_uses_remote_tracking_refs", "FetchAsync CheckedOutWorktreeBranch UsesRemoteTrackingRefs", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string worktreeDir = Path.Combine(rootDir, "worktree");

                try
                {
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
            }));

            cases.Add(CaseAsync("diff_async_no_merge_base_falls_back_to_two_dot_diff", "DiffAsync NoMergeBase FallsBackToTwoDotDiff", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestGitRepoHelper.CreateWorkingRepoCopy();

                try
                {
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
            }));

            cases.Add(CaseAsync("merge_branch_local_async_cleans_conflict_state_after_failure", "MergeBranchLocalAsync Cleans Conflict State After Failure", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
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

                    InvalidOperationException? mergeEx = null;
                    try
                    {
                        await service.MergeBranchLocalAsync(targetDir, bareDir, "armada/conflict", "main").ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        mergeEx = ex;
                    }

                    string status = (await RunGitAsync(targetDir, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false)).Trim();
                    string currentBranch = (await RunGitAsync(targetDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    string fileContents = await File.ReadAllTextAsync(Path.Combine(targetDir, "README.md")).ConfigureAwait(false);

                    AssertNotNull(mergeEx, "Conflicting landing merge should throw");
                    AssertTrue(mergeEx is GitCommandException, "Conflicting merge should surface the failed git command, got " + mergeEx!.GetType().Name);
                    GitCommandException gitEx = (GitCommandException)mergeEx;
                    AssertEqual("merge", gitEx.Arguments.FirstOrDefault(), "the failed command is the merge");
                    AssertEqual(1, gitEx.ExitCode, "git merge exits 1 when it stops on conflicts");
                    AssertEqual(String.Empty, status, "Conflict cleanup should leave no staged or unmerged changes");
                    AssertEqual(String.Empty, (await RunGitAsync(targetDir, "ls-files", "-u", "-z").ConfigureAwait(false)), "Conflict cleanup should leave no unmerged index entries");
                    AssertEqual("main", currentBranch, "Conflict cleanup should return to the target branch");
                    AssertEqual("target change\n", fileContents, "Conflict cleanup should restore the pre-merge working tree");
                    AssertEqual(String.Empty, (await RunGitAsync(targetDir, "for-each-ref", "refs/heads/armada-landing/").ConfigureAwait(false)).Trim(), "A failed landing must not leave armada-landing/* branches either");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("merge_branch_local_async_unions_only_changelog", "MergeBranchLocalAsync Keeps Both Changelog Entries", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "CHANGELOG.md"), "base entry\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "CHANGELOG.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Base changelog").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/conflict").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "CHANGELOG.md"), "branch change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Branch change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/conflict").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, "CHANGELOG.md"), "target change\n").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "commit", "-am", "Target change").ConfigureAwait(false);

                    await service.MergeBranchLocalAsync(targetDir, bareDir, "armada/conflict", "main").ConfigureAwait(false);
                    string content = await File.ReadAllTextAsync(Path.Combine(targetDir, "CHANGELOG.md")).ConfigureAwait(false);
                    AssertContains("branch change", content);
                    AssertContains("target change", content);
                    AssertEqual(String.Empty, (await RunGitAsync(targetDir, "ls-files", "-u").ConfigureAwait(false)).Trim());
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("landing_sync_integrates_current_target", "Landing synchronization integrates the current target into the stale mission branch", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "CHANGELOG.md"), "base entry\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "CHANGELOG.md").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Base changelog").ConfigureAwait(false);
                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/conflict").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "CHANGELOG.md"), "branch change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Branch change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/conflict").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(targetDir, "CHANGELOG.md"), "target change\n").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "commit", "-am", "Target change").ConfigureAwait(false);

                    string? headBefore = await service.GetHeadCommitHashAsync(sourceDir).ConfigureAwait(false);
                    LandingTargetSynchronizationResult changed = await LandingTargetSynchronization.SynchronizeAsync(service, sourceDir, "main", targetDir).ConfigureAwait(false);
                    AssertTrue(changed.Advanced, "the mission dock head advances when target moved");
                    AssertEqual(headBefore, changed.Before, "Before is the dock head before integrating");
                    AssertEqual(await service.GetHeadCommitHashAsync(sourceDir).ConfigureAwait(false), changed.After, "After is the dock head after integrating");
                    AssertEqual((await RunGitAsync(targetDir, "rev-parse", "main").ConfigureAwait(false)).Trim(), changed.LocalTargetTip, "the local target tip integrated against is recorded");
                    string content = await File.ReadAllTextAsync(Path.Combine(sourceDir, "CHANGELOG.md")).ConfigureAwait(false);
                    AssertContains("branch change", content);
                    AssertContains("target change", content);
                    AssertEqual(String.Empty, (await RunGitAsync(sourceDir, "ls-files", "-u").ConfigureAwait(false)).Trim());
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("merge_branch_local_async_succeeds_when_target_checkout_is_a_git_worktree", "MergeBranchLocalAsync Succeeds When TargetCheckout Is A GitWorktree", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetRepoDir = Path.Combine(rootDir, "target");
                string landingWorktreeDir = Path.Combine(rootDir, "landing-worktree");

                try
                {
                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "-b", "armada/worktree-merge").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "README.md"), "base\nworker change\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-am", "Worker change").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/worktree-merge").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", bareDir, targetRepoDir).ConfigureAwait(false);
                    await RunGitAsync(targetRepoDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetRepoDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);
                    await RunGitAsync(targetRepoDir, "checkout", "-b", "hold").ConfigureAwait(false);
                    await RunGitAsync(targetRepoDir, "worktree", "add", landingWorktreeDir, "main").ConfigureAwait(false);

                    await service.MergeBranchLocalAsync(landingWorktreeDir, bareDir, "armada/worktree-merge", "main").ConfigureAwait(false);

                    string currentBranch = (await RunGitAsync(landingWorktreeDir, "rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false)).Trim();
                    string mergedReadme = await File.ReadAllTextAsync(Path.Combine(landingWorktreeDir, "README.md")).ConfigureAwait(false);

                    AssertEqual("main", currentBranch, "Landing worktree should stay on the target branch");
                    AssertEqual("base\nworker change\n", mergedReadme, "Landing merge should succeed in a git worktree checkout");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("merge_branch_local_async_materializes_missing_target_branch_in_landing_checkout", "MergeBranchLocalAsync Materializes MissingTargetBranch In Landing Checkout", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
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
                    string mergedReadme = await RunGitAsync(targetDir, "show", "armada-v050-live:README.md").ConfigureAwait(false);
                    string targetBranchFile = await RunGitAsync(targetDir, "show", "armada-v050-live:target-only.txt").ConfigureAwait(false);

                    AssertEqual("main", currentBranch, "Landing restores the branch the user's checkout was on");
                    AssertTrue(!String.IsNullOrWhiteSpace(localBranch), "Landing checkout should create a local target branch when it is missing");
                    AssertEqual("base\nworker change\n", mergedReadme, "Landing merge should include worker changes");
                    AssertEqual("target branch content\n", targetBranchFile, "Landing merge should preserve target branch files");

                    string landingRefs = (await RunGitAsync(targetDir, "for-each-ref", "refs/heads/armada-landing/").ConfigureAwait(false)).Trim();
                    AssertEqual(String.Empty, landingRefs, "Landing must not leave armada-landing/* branches in the user's checkout");
                }
                finally
                {
                    if (Directory.Exists(rootDir))
                    {
                        try { Directory.Delete(rootDir, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("merge_branch_local_async_dirty_landing_checkout_throws_before_merge", "MergeBranchLocalAsync DirtyLandingCheckout Throws Before Merge", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
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
                    AssertFalse(ex is GitCommandException, "the dirty landing checkout is rejected by Armada's check, not by a failed git command");
                    AssertTrue(ex is GitDirtyCheckoutException, "Dirty landing checkout is rejected with GitDirtyCheckoutException, got " + ex!.GetType().Name);
                    AssertEqual(targetDir, ((GitDirtyCheckoutException)ex).CheckoutPath, "the exception names the dirty checkout");
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
            }));

            cases.Add(CaseAsync("force_advance_branch_lifts_detached_commit", "ForceAdvanceBranchAsync lifts a detached commit onto the branch ref", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();

                try
                {
                    // A pipeline stage branch created at main, then a commit produced on a DETACHED HEAD --
                    // exactly the stage-lag case where the branch ref is left behind the produced work.
                    await RunGitAsync(repo, "branch", "armada/stage", "HEAD").ConfigureAwait(false);
                    string branchStart = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();

                    await RunGitAsync(repo, "checkout", "--detach", "HEAD").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "stage-work.txt"), "produced by the prior stage").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "stage work").ConfigureAwait(false);
                    string producedCommit = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    string beforeRef = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();
                    AssertEqual(branchStart, beforeRef, "branch ref should lag the detached produced commit before hardening");
                    AssertNotEqual(producedCommit, beforeRef, "produced commit should differ from the lagging branch ref");

                    bool advanced = await service.ForceAdvanceBranchAsync(repo, "armada/stage", producedCommit).ConfigureAwait(false);
                    AssertTrue(advanced, "force-advance should succeed");

                    string afterRef = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();
                    AssertEqual(producedCommit, afterRef, "branch ref should point at the produced commit after hardening");
                }
                finally
                {
                    if (Directory.Exists(repo))
                    {
                        try { Directory.Delete(repo, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("stage_handoff_never_rewinds_a_newer_tip", "A stale stage handoff cannot rewind a branch that already contains its commit", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();

                try
                {
                    // A pipeline stage branch created at main, then a commit produced on a DETACHED HEAD --
                    // exactly the stage-lag case where the branch ref is left behind the produced work.
                    await RunGitAsync(repo, "branch", "armada/stage", "HEAD").ConfigureAwait(false);
                    string branchStart = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();

                    await RunGitAsync(repo, "checkout", "--detach", "HEAD").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "stage-work.txt"), "produced by the prior stage").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "stage work").ConfigureAwait(false);
                    string producedCommit = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    string beforeRef = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();
                    AssertEqual(branchStart, beforeRef, "branch ref should lag the detached produced commit before hardening");
                    AssertNotEqual(producedCommit, beforeRef, "produced commit should differ from the lagging branch ref");

                    bool advanced = await service.ForceAdvanceBranchAsync(repo, "armada/stage", producedCommit).ConfigureAwait(false);
                    AssertTrue(advanced, "force-advance should succeed");

                    string afterRef = (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim();
                    AssertEqual(producedCommit, afterRef, "branch ref should point at the produced commit after hardening");
                    AssertTrue(await service.ForceAdvanceBranchAsync(repo, "armada/stage", branchStart).ConfigureAwait(false), "Already integrated old work is a no-op");
                    AssertEqual(producedCommit, (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim(), "A newer branch tip must survive stale handoff");
                }
                finally
                {
                    if (Directory.Exists(repo))
                    {
                        try { Directory.Delete(repo, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("stage_handoff_preserves_divergent_work", "Divergent stage commits are preserved without replacing another writer's branch", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    string start = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    await RunGitAsync(repo, "checkout", "--detach", start).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "stage.txt"), "stage output").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "stage output").ConfigureAwait(false);
                    string produced = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    await RunGitAsync(repo, "checkout", "--detach", start).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "other.txt"), "another writer").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "other output").ConfigureAwait(false);
                    string other = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    await RunGitAsync(repo, "branch", "armada/stage", other).ConfigureAwait(false);
                    AssertFalse(await service.ForceAdvanceBranchAsync(repo, "armada/stage", produced).ConfigureAwait(false));
                    AssertEqual(other, (await RunGitAsync(repo, "rev-parse", "armada/stage").ConfigureAwait(false)).Trim());
                    AssertEqual(produced, (await RunGitAsync(repo, "rev-parse", "refs/armada/stage-commits/" + produced).ConfigureAwait(false)).Trim(), "Rejected work remains reachable after dock cleanup");
                }
                finally { try { Directory.Delete(repo, true); } catch { } }
            }));

            cases.Add(CaseAsync("merge_commit_merges_skips_contained_and_aborts_conflicts", "MergeCommitAsync merges a commit, skips one already contained, aborts a conflict cleanly and reports a missing commit", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();

                try
                {
                    string baseCommit = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    string sliceA = await CommitOnNewBranchAsync(repo, "slice-a", baseCommit, "a.txt", "from a").ConfigureAwait(false);
                    string sliceB = await CommitOnNewBranchAsync(repo, "slice-b", baseCommit, "shared.txt", "from b").ConfigureAwait(false);
                    string sliceC = await CommitOnNewBranchAsync(repo, "slice-c", baseCommit, "shared.txt", "from c").ConfigureAwait(false);
                    await RunGitAsync(repo, "checkout", "-q", "-b", "integration", baseCommit).ConfigureAwait(false);

                    GitCommitMergeResult merged = await service.MergeCommitAsync(repo, sliceA, null, "integrate a").ConfigureAwait(false);
                    AssertEqual(GitCommitMergeOutcomeEnum.Merged, merged.Outcome, merged.Message);
                    AssertTrue(File.Exists(Path.Combine(repo, "a.txt")), "the merged slice's file is on the branch");

                    GitCommitMergeResult again = await service.MergeCommitAsync(repo, sliceA, null, "integrate a").ConfigureAwait(false);
                    AssertEqual(GitCommitMergeOutcomeEnum.AlreadyContained, again.Outcome);

                    AssertEqual(GitCommitMergeOutcomeEnum.Merged, (await service.MergeCommitAsync(repo, sliceB, null, "integrate b").ConfigureAwait(false)).Outcome);
                    string beforeConflict = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                    GitCommitMergeResult conflict = await service.MergeCommitAsync(repo, sliceC, null, "integrate c").ConfigureAwait(false);
                    AssertEqual(GitCommitMergeOutcomeEnum.Conflicted, conflict.Outcome);
                    AssertTrue(conflict.ConflictedFiles.Contains("shared.txt"), "the conflicted file is named");
                    AssertEqual(beforeConflict, (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim(), "an aborted merge leaves HEAD alone");
                    AssertEqual(String.Empty, (await RunGitAsync(repo, "status", "--porcelain", "--untracked-files=no").ConfigureAwait(false)).Trim(), "no conflict markers are left behind");
                    AssertFalse(File.Exists(Path.Combine(repo, ".git", "MERGE_HEAD")), "the worktree is not left mid-merge");

                    GitCommitMergeResult missing = await service.MergeCommitAsync(repo, "0123456789abcdef0123456789abcdef01234567", "no-such-branch", "integrate missing").ConfigureAwait(false);
                    AssertEqual(GitCommitMergeOutcomeEnum.Missing, missing.Outcome);
                }
                finally
                {
                    if (Directory.Exists(repo))
                    {
                        try { Directory.Delete(repo, true); }
                        catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("force_advance_branch_empty_args_returns_false", "ForceAdvanceBranchAsync returns false for empty arguments", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                AssertFalse(await service.ForceAdvanceBranchAsync("", "b", "c").ConfigureAwait(false), "empty worktree path should return false");
                AssertFalse(await service.ForceAdvanceBranchAsync("/tmp/repo", "", "c").ConfigureAwait(false), "empty branch name should return false");
                AssertFalse(await service.ForceAdvanceBranchAsync("/tmp/repo", "b", "").ConfigureAwait(false), "empty commit hash should return false");
            }));

            cases.Add(CaseAsync("recent_commits_for_paths_reports_changes", "GetRecentCommitsForPathsAsync reports commits touching a path", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    string dir = Path.Combine(repo, "anchors");
                    Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(Path.Combine(dir, "widget.cs").Replace('/', Path.DirectorySeparatorChar), "class Widget {}").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "add widget").ConfigureAwait(false);

                    System.Collections.Generic.IReadOnlyList<string> commits =
                        await service.GetRecentCommitsForPathsAsync(repo, new System.Collections.Generic.List<string> { "anchors/widget.cs" }, 3).ConfigureAwait(false);
                    AssertTrue(commits.Count >= 1, "expected at least one commit for the changed path");
                    AssertTrue(commits[0].Contains("add widget"), "the commit subject should be reported");
                }
                finally
                {
                    if (Directory.Exists(repo)) { try { Directory.Delete(repo, true); } catch { } }
                }
            }));

            cases.Add(CaseAsync("subject_terms_found_in_tree", "FindExistingSubjectTermsAsync finds a term present in the tree", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(repo, "QuarantineReason.cs"), "public class QuarantineReason {}").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "add term").ConfigureAwait(false);

                    System.Collections.Generic.IReadOnlyList<string> found =
                        await service.FindExistingSubjectTermsAsync(repo, new System.Collections.Generic.List<string> { "QuarantineReason", "TotallyAbsentTerm" }).ConfigureAwait(false);
                    AssertTrue(found.Contains("QuarantineReason"), "a present term should be reported");
                    AssertFalse(found.Contains("TotallyAbsentTerm"), "an absent term should not be reported");
                }
                finally
                {
                    if (Directory.Exists(repo)) { try { Directory.Delete(repo, true); } catch { } }
                }
            }));

            cases.Add(CaseAsync("force_advance_branch_invalid_commit_returns_false", "ForceAdvanceBranchAsync returns false when the commit does not exist", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repo = TestGitRepoHelper.CreateWorkingRepoCopy();

                try
                {
                    // A ref update to a non-existent object must fail closed, not throw or silently succeed.
                    bool advanced = await service.ForceAdvanceBranchAsync(repo, "armada/stage", "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef").ConfigureAwait(false);
                    AssertFalse(advanced, "advancing to a non-existent commit should fail closed");
                }
                finally
                {
                    if (Directory.Exists(repo))
                    {
                        try { Directory.Delete(repo, true); }
                        catch { }
                    }
                }
            }));


            cases.Add(CaseAsync("branch_exists_does_not_treat_name_as_glob", "BranchExistsAsync matches the exact ref, not a glob", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await RunGitAsync(repoDir, "branch", "feature-one").ConfigureAwait(false);

                    // branch --list treats its argument as a pattern, so "feature-*" used to report true.
                    AssertFalse(await service.BranchExistsAsync(repoDir, "feature-*").ConfigureAwait(false), "a glob must not match an existing branch");
                    AssertTrue(await service.BranchExistsAsync(repoDir, "feature-one").ConfigureAwait(false), "the exact branch exists");
                    AssertFalse(await service.BranchExistsAsync(repoDir, "feature").ConfigureAwait(false), "a prefix is not a branch");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("get_branch_changes_reports_deletes_renames_and_counts", "GetBranchChangesAsync reports deletions, both rename sides, unusual names, and line counts", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    Directory.CreateDirectory(Path.Combine(repoDir, "protected"));
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "protected", "keep.txt"), "a\nb\n").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "protected", "move.txt"), "m\n").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "commit", "-m", "base files").ConfigureAwait(false);

                    await RunGitAsync(repoDir, "checkout", "-b", "armada/changes").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "rm", "-q", "protected/keep.txt").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "mv", "protected/move.txt", "moved.txt").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "caf\u00e9 x.txt"), "1\n2\n3\n").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "commit", "-m", "changes").ConfigureAwait(false);

                    IReadOnlyList<Armada.Core.Models.GitChangedFile> changes = await service.GetBranchChangesAsync(repoDir, "main").ConfigureAwait(false);
                    Dictionary<string, Armada.Core.Models.GitChangedFile> byPath = new Dictionary<string, Armada.Core.Models.GitChangedFile>(StringComparer.Ordinal);
                    foreach (Armada.Core.Models.GitChangedFile change in changes) byPath[change.Path] = change;

                    AssertTrue(byPath.ContainsKey("protected/keep.txt"), "deleted path reported");
                    AssertEqual(Armada.Core.Enums.GitChangeKindEnum.Deleted, byPath["protected/keep.txt"].Kind);
                    AssertEqual(2, byPath["protected/keep.txt"].DeletedLines);
                    AssertTrue(byPath.ContainsKey("protected/move.txt"), "rename source reported");
                    AssertTrue(byPath.ContainsKey("moved.txt"), "rename target reported");
                    AssertTrue(byPath.ContainsKey("caf\u00e9 x.txt"), "non-ASCII path reported verbatim, not C-quoted");
                    AssertEqual(3, byPath["caf\u00e9 x.txt"].AddedLines);
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("changed_files_since_reports_unquoted_paths", "GetChangedFilesSinceAsync returns non-ASCII paths verbatim", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    string start = (await RunGitAsync(repoDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "na\u00efve.txt"), "x\n").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "add", "-A").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "commit", "-m", "unicode").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "untracked \u00e9.txt"), "y\n").ConfigureAwait(false);

                    IReadOnlyList<string> changed = await service.GetChangedFilesSinceAsync(repoDir, start).ConfigureAwait(false);
                    AssertTrue(changed.Contains("na\u00efve.txt"), "committed non-ASCII path should be verbatim");
                    AssertTrue(changed.Contains("untracked \u00e9.txt"), "untracked non-ASCII path should be verbatim");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("merge_branch_local_async_unrelated_histories_merges", "MergeBranchLocalAsync merges a branch with no common ancestor", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string rootDir = TestTemp.NewDirectory("gitservice");
                string sourceDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                string bareDir = Path.Combine(rootDir, "bare.git");
                string targetDir = Path.Combine(rootDir, "target");

                try
                {
                    await RunGitAsync(rootDir, "clone", "--bare", sourceDir, bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "remote", "add", "armada", bareDir).ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "checkout", "--orphan", "armada/orphan").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "rm", "-rf", "-q", ".").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(sourceDir, "orphan.txt"), "orphan\n").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "add", "orphan.txt").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "commit", "-m", "Orphan").ConfigureAwait(false);
                    await RunGitAsync(sourceDir, "push", "armada", "armada/orphan").ConfigureAwait(false);

                    await RunGitAsync(rootDir, "clone", bareDir, targetDir).ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.name", "Armada Tests").ConfigureAwait(false);
                    await RunGitAsync(targetDir, "config", "user.email", "armada-tests@example.com").ConfigureAwait(false);

                    await service.MergeBranchLocalAsync(targetDir, bareDir, "armada/orphan", "main").ConfigureAwait(false);

                    AssertTrue(File.Exists(Path.Combine(targetDir, "orphan.txt")), "orphan branch content should be merged");
                    AssertTrue(File.Exists(Path.Combine(targetDir, "README.md")), "target content should remain");
                    string parents = (await RunGitAsync(targetDir, "rev-list", "--parents", "-n", "1", "HEAD").ConfigureAwait(false)).Trim();
                    AssertEqual(3, parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, "HEAD should be a merge commit");
                }
                finally
                {
                    TestTemp.TryDelete(rootDir);
                }
            }));

            cases.Add(CaseAsync("list_branches_reports_utc_commit_dates", "ListBranchesAsync parses iso-strict commit dates as UTC", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    IReadOnlyList<Armada.Core.Models.BranchInfo> branches = await service.ListBranchesAsync(repoDir, "main").ConfigureAwait(false);
                    AssertEqual(1, branches.Count);
                    AssertNotNull(branches[0].CommitDate, "commit date should parse");
                    AssertEqual(DateTimeKind.Utc, branches[0].CommitDate!.Value.Kind);
                    AssertTrue(Math.Abs((DateTime.UtcNow - branches[0].CommitDate!.Value).TotalDays) < 7, "commit date should be recent");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("list_branch_tips_then_measure_only_the_page", "ListBranchTipsAsync lists without divergence; MeasureBranchDivergenceAsync fills only the branches given", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    foreach (string name in new string[] { "alpha", "beta" })
                    {
                        await RunGitAsync(repoDir, "checkout", "-b", name, "main").ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(repoDir, name + ".txt"), name + "\n").ConfigureAwait(false);
                        await RunGitAsync(repoDir, "add", "-A").ConfigureAwait(false);
                        await RunGitAsync(repoDir, "commit", "-m", name).ConfigureAwait(false);
                    }
                    await RunGitAsync(repoDir, "checkout", "main").ConfigureAwait(false);

                    IReadOnlyList<Armada.Core.Models.BranchInfo> tips = await service.ListBranchTipsAsync(repoDir, "main").ConfigureAwait(false);
                    AssertEqual(3, tips.Count);
                    AssertEqual("main", tips[0].Name, "default branch first");
                    AssertTrue(tips[0].IsDefault, "default branch flagged");
                    AssertTrue(tips.All(b => b.Ahead == 0 && b.Behind == 0), "tips carry no divergence");

                    List<Armada.Core.Models.BranchInfo> page = new List<Armada.Core.Models.BranchInfo> { tips[1] };
                    await service.MeasureBranchDivergenceAsync(repoDir, "main", page).ConfigureAwait(false);
                    AssertEqual("alpha", tips[1].Name);
                    AssertEqual(1, tips[1].Ahead, "alpha is one commit ahead");
                    AssertEqual(0, tips[1].Behind);
                    AssertEqual(0, tips[2].Ahead, "beta was not on the page, so it was not measured");

                    IReadOnlyList<Armada.Core.Models.BranchInfo> full = await service.ListBranchesAsync(repoDir, "main").ConfigureAwait(false);
                    AssertEqual(1, full.First(b => b.Name == "beta").Ahead, "ListBranchesAsync still measures every branch");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("list_branches_resolves_local_branch_when_tag_shares_name", "ListBranchesAsync measures the local branch when a tag shares its name", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    string shortName = "shared-tip";
                    string mainCommit = (await RunGitAsync(repoDir, "rev-parse", "refs/heads/main").ConfigureAwait(false)).Trim();
                    await CommitOnNewBranchAsync(repoDir, shortName, mainCommit, "shared-tip.txt", "branch tip\n").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "tag", shortName, mainCommit).ConfigureAwait(false);
                    await RunGitAsync(repoDir, "checkout", "-q", "main").ConfigureAwait(false);

                    IReadOnlyList<BranchInfo> branches = await service.ListBranchesAsync(repoDir, "main").ConfigureAwait(false);
                    BranchInfo listed = RequireListedBranch(branches, shortName);
                    AssertEqual(shortName, listed.Name, "a same-named tag must leave the listed branch name unchanged");
                    AssertEqual(1, listed.Ahead, "the local branch has one commit beyond main");
                    AssertEqual(0, listed.Behind, "the local branch is not behind main");
                    AssertUnavailableDivergenceAbsent(listed);
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("list_branches_marks_missing_default_divergence_unavailable", "ListBranchesAsync reports missing default branch counts as unavailable", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    IReadOnlyList<BranchInfo> branches = await service.ListBranchesAsync(repoDir, "missing-default").ConfigureAwait(false);
                    BranchInfo branch = RequireListedBranch(branches, "main");
                    AssertUnavailableDivergence(branch, "a missing default ref");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("list_branches_marks_invalid_count_output_unavailable", "ListBranchesAsync reports malformed divergence output as unavailable", TestTags.Negative, async () =>
            {
                GitService service = CreateService(new BranchListCommandExecutor("not a count"));
                IReadOnlyList<BranchInfo> branches = await service.ListBranchesAsync("remote-repository", "main").ConfigureAwait(false);
                AssertUnavailableDivergence(RequireListedBranch(branches, "topic"), "malformed rev-list output");
            }));

            cases.Add(CaseAsync("list_branches_marks_failed_comparison_unavailable", "ListBranchesAsync reports failed divergence commands as unavailable", TestTags.Negative, async () =>
            {
                GitService service = CreateService(new BranchListCommandExecutor(null));
                IReadOnlyList<BranchInfo> branches = await service.ListBranchesAsync("remote-repository", "main").ConfigureAwait(false);
                AssertUnavailableDivergence(RequireListedBranch(branches, "topic"), "a failed rev-list command");
            }));

            cases.Add(CaseAsync("failed_git_command_throws_typed_exception", "A failing git command throws GitCommandException with the exit code", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    GitCommandException? failure = null;
                    try
                    {
                        await service.DeleteLocalBranchAsync(repoDir, "does-not-exist").ConfigureAwait(false);
                    }
                    catch (GitCommandException ex)
                    {
                        failure = ex;
                    }

                    AssertNotNull(failure, "deleting a missing branch should throw GitCommandException");
                    AssertNotEqual(0, failure!.ExitCode);
                    AssertEqual("git", failure.Executable);
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(Case("git_command_exception_message_is_one_line_with_all_of_stderr", "GitCommandException.Message keeps every stderr line on one log line", TestTags.Negative, () =>
            {
                // A multi-line message is split across log lines (and syslog records), so the line naming the cause
                // ("fatal: detected dubious ownership ...") was lost when only the first line was kept or grepped.
                string stderr = "Cloning into bare repository '/app/data/repos/x.git'...\nfatal: detected dubious ownership in repository at '/iv/origin.git'\r\n\nfatal: Could not read from remote repository.\n";
                GitCommandException ex = new GitCommandException("git", new List<string> { "clone", "--bare" }, 128, String.Empty, stderr);
                AssertFalse(ex.Message.Contains('\n'), "message has no line feed");
                AssertFalse(ex.Message.Contains('\r'), "message has no carriage return");
                AssertEqual("git failed (exit 128): Cloning into bare repository '/app/data/repos/x.git'... | fatal: detected dubious ownership in repository at '/iv/origin.git' | fatal: Could not read from remote repository.", ex.Message);
                AssertEqual(stderr, ex.StandardError, "StandardError keeps the raw text");
            }));

            cases.Add(CaseAsync("working_tree_status_counts_from_porcelain_v2", "GetWorkingTreeStatusAsync counts renames once and untracked unusual names", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await RunGitAsync(repoDir, "mv", "README.md", "READ ME.md").ConfigureAwait(false);
                    // Windows does not allow ">" in file names; there the unusual untracked name has spaces and non-ASCII instead.
                    string unusualName = OperatingSystem.IsWindows() ? "caf\u00e9 a - b.txt" : "a -> b.txt";
                    await File.WriteAllTextAsync(Path.Combine(repoDir, unusualName), "x\n").ConfigureAwait(false);

                    Armada.Core.Models.GitWorkingTreeStatus status = await service.GetWorkingTreeStatusAsync(repoDir).ConfigureAwait(false);
                    AssertEqual(1, status.ModifiedCount);
                    AssertEqual(1, status.UntrackedCount);
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("commit_all_leaves_untracked_scratch_files_out", "CommitAllAsync commits the work and leaves untracked scratch files in the worktree", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await RunGitAsync(repoDir, "config", "user.email", "t@t").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "config", "user.name", "t").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "Feature.cs"), "class Feature { }\n").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "README.md.backup"), "old\n").ConfigureAwait(false);
                    Directory.CreateDirectory(Path.Combine(repoDir, "src"));
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "src", "Program.cs.orig"), "old\n").ConfigureAwait(false);

                    bool committed = await service.CommitAllAsync(repoDir, "leftover").ConfigureAwait(false);

                    AssertTrue(committed, "the real change is committed");
                    string files = await RunGitAsync(repoDir, "show", "--name-only", "--format=", "HEAD").ConfigureAwait(false);
                    AssertContains("Feature.cs", files);
                    AssertFalse(files.Contains("README.md.backup"), "the backup file is not committed");
                    AssertFalse(files.Contains("Program.cs.orig"), "the merge leftover is not committed");
                    AssertTrue(File.Exists(Path.Combine(repoDir, "README.md.backup")), "the scratch file stays in the worktree");
                    string status = await RunGitAsync(repoDir, "status", "--porcelain", "--untracked-files=all").ConfigureAwait(false);
                    AssertContains("?? README.md.backup", status, "the scratch file is left untracked, not staged");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("commit_all_with_only_scratch_files_commits_nothing", "CommitAllAsync makes no commit when only scratch files are left", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await RunGitAsync(repoDir, "config", "user.email", "t@t").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "config", "user.name", "t").ConfigureAwait(false);
                    string headBefore = (await RunGitAsync(repoDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "README.md.backup"), "old\n").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "notes.tmp"), "x\n").ConfigureAwait(false);

                    bool committed = await service.CommitAllAsync(repoDir, "leftover").ConfigureAwait(false);

                    AssertFalse(committed, "nothing but scratch files: no commit");
                    string headAfter = (await RunGitAsync(repoDir, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                    AssertEqual(headBefore, headAfter, "HEAD is unchanged");
                    string staged = await RunGitAsync(repoDir, "diff", "--cached", "--name-only").ConfigureAwait(false);
                    AssertEqual("", staged.Trim(), "nothing is left staged");
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(CaseAsync("commit_all_keeps_tracked_files_with_scratch_names", "CommitAllAsync still commits a change to a tracked file whose name looks like scratch", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string repoDir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    await RunGitAsync(repoDir, "config", "user.email", "t@t").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "config", "user.name", "t").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "fixture.bak"), "v1\n").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "add", "fixture.bak").ConfigureAwait(false);
                    await RunGitAsync(repoDir, "commit", "-q", "-m", "track fixture").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repoDir, "fixture.bak"), "v2\n").ConfigureAwait(false);

                    bool committed = await service.CommitAllAsync(repoDir, "leftover").ConfigureAwait(false);

                    AssertTrue(committed, "a tracked file is the repository's own, not scratch");
                    string files = await RunGitAsync(repoDir, "show", "--name-only", "--format=", "HEAD").ConfigureAwait(false);
                    AssertContains("fixture.bak", files);
                }
                finally
                {
                    TestTemp.TryDelete(repoDir);
                }
            }));

            cases.Add(Case("scratch_file_matcher_recognizes_backups_and_rejects_source", "ScratchFileMatcher matches backups and leftovers but not source files", TestTags.Negative, () =>
            {
                AssertTrue(ScratchFileMatcher.IsScratchFile("README.md.backup"));
                AssertTrue(ScratchFileMatcher.IsScratchFile("src/Program.cs.ORIG"), "case-insensitive, nested");
                AssertTrue(ScratchFileMatcher.IsScratchFile("src\\patch.rej"), "back slashes");
                AssertTrue(ScratchFileMatcher.IsScratchFile("docs/.DS_Store"));
                AssertTrue(ScratchFileMatcher.IsScratchFile("Notes.md~"));
                AssertFalse(ScratchFileMatcher.IsScratchFile("src/Program.cs"));
                AssertFalse(ScratchFileMatcher.IsScratchFile(".bak"), "a bare suffix is a name, not a backup");
                AssertFalse(ScratchFileMatcher.IsScratchFile("backup/Program.cs"), "a directory name does not count");
                AssertFalse(ScratchFileMatcher.IsScratchFile(""));
                AssertFalse(ScratchFileMatcher.IsScratchFile(null));
            }));

            cases.Add(CaseAsync("fast_forward_dirty_checkout_reports_exact_paths", "Fast-forwarding a verified commit into a dirty checkout reports exactly the modified and untracked paths", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string source = TestGitRepoHelper.CreateWorkingRepoCopy();
                string parent = TestTemp.NewDirectory("ff_dirty");
                string target = Path.Combine(parent, "target");
                await RunGitAsync(parent, "clone", "-q", source, target).ConfigureAwait(false);
                string verified = await CommitOnNewBranchAsync(source, "verified", "main", "landed.txt", "landed\n").ConfigureAwait(false);
                string targetHead = (await RunGitAsync(target, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
                await File.WriteAllTextAsync(Path.Combine(target, "README.md"), "locally edited\n").ConfigureAwait(false);
                Directory.CreateDirectory(Path.Combine(target, "notes"));
                await File.WriteAllTextAsync(Path.Combine(target, "notes", "scratch.txt"), "untracked\n").ConfigureAwait(false);

                GitDirtyCheckoutException? dirty = null;
                try { await service.FastForwardVerifiedCommitAsync(target, source, verified, "main").ConfigureAwait(false); }
                catch (GitDirtyCheckoutException ex) { dirty = ex; }

                AssertNotNull(dirty, "a dirty checkout is refused");
                AssertEqual(target, dirty!.CheckoutPath);
                List<string> paths = new List<string>(dirty.ModifiedPaths);
                paths.Sort(StringComparer.Ordinal);
                AssertEqual("README.md|notes/scratch.txt", String.Join("|", paths), "ModifiedPaths are the real porcelain paths, not prose");
                AssertEqual(targetHead, (await RunGitAsync(target, "rev-parse", "HEAD").ConfigureAwait(false)).Trim(), "the checkout is not moved");
                AssertEqual("locally edited\n", await File.ReadAllTextAsync(Path.Combine(target, "README.md")).ConfigureAwait(false), "local edits are preserved");
            }));

            cases.Add(CaseAsync("fast_forward_clean_checkout_lands_verified_commit", "Fast-forwarding a verified commit into a clean checkout moves it to exactly that commit", TestTags.Positive, async () =>
            {
                GitService service = CreateService();
                string source = TestGitRepoHelper.CreateWorkingRepoCopy();
                string parent = TestTemp.NewDirectory("ff_clean");
                string target = Path.Combine(parent, "target");
                await RunGitAsync(parent, "clone", "-q", source, target).ConfigureAwait(false);
                await RunGitAsync(source, "checkout", "-q", "main").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(source, "landed.txt"), "landed\n").ConfigureAwait(false);
                await RunGitAsync(source, "add", "-A").ConfigureAwait(false);
                await RunGitAsync(source, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "-m", "verified").ConfigureAwait(false);
                string verified = (await RunGitAsync(source, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                await service.FastForwardVerifiedCommitAsync(target, source, verified, "main").ConfigureAwait(false);

                AssertEqual(verified, (await RunGitAsync(target, "rev-parse", "HEAD").ConfigureAwait(false)).Trim());
                AssertTrue(File.Exists(Path.Combine(target, "landed.txt")), "the verified change is in the checkout");
            }));

            cases.Add(Case("landing_target_advanced_payload_carries_before_and_after", "The mission.landing_target_advanced payload carries the dock head before and after and the integrated target tips", TestTags.Positive, () =>
            {
                LandingTargetSynchronizationResult result = new LandingTargetSynchronizationResult
                {
                    Before = "aaa111", After = "bbb222", RemoteTargetTip = "ccc333", LocalTargetTip = "ddd444"
                };
                AssertTrue(result.Advanced);
                LandingTargetAdvancedPayload payload = LandingTargetSynchronization.ToAdvancedPayload(result, "main");
                string json = System.Text.Json.JsonSerializer.Serialize(payload);
                LandingTargetAdvancedPayload read = System.Text.Json.JsonSerializer.Deserialize<LandingTargetAdvancedPayload>(json)!;
                AssertEqual("main", read.TargetBranch);
                AssertEqual("aaa111", read.Before);
                AssertEqual("bbb222", read.After);
                AssertEqual("ccc333", read.RemoteTargetTip);
                AssertEqual("ddd444", read.LocalTargetTip);
                AssertFalse(new LandingTargetSynchronizationResult { Before = "same", After = "same" }.Advanced, "an unchanged head is not an advance");
            }));

            cases.Add(CaseAsync("landing_sync_unchanged_target_reports_same_head", "Landing synchronization against a target the dock already contains reports Before equal to After", TestTags.Negative, async () =>
            {
                GitService service = CreateService();
                string origin = TestGitRepoHelper.CreateBareRepoCopy();
                string parent = TestTemp.NewDirectory("sync_same");
                string dock = Path.Combine(parent, "dock");
                await RunGitAsync(parent, "clone", "-q", origin, dock).ConfigureAwait(false);
                string head = (await RunGitAsync(dock, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                LandingTargetSynchronizationResult result = await LandingTargetSynchronization.SynchronizeAsync(service, dock, "main", null).ConfigureAwait(false);

                AssertFalse(result.Advanced, "nothing to integrate");
                AssertEqual(head, result.Before);
                AssertEqual(head, result.After);
                AssertEqual(head, result.RemoteTargetTip, "the remote tip integrated against is recorded");
                AssertNull(result.LocalTargetTip, "no local checkout was given");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.GitService",
                displayName: "Git Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static GitService CreateService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new GitService(logging);
        }

        private static GitService CreateService(IHostCommandExecutor commands)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new GitService(logging, commands);
        }

        private static BranchInfo RequireListedBranch(IReadOnlyList<BranchInfo> branches, string shortName)
        {
            BranchInfo? found = null;
            foreach (BranchInfo branch in branches)
            {
                if (String.Equals(branch.Name, shortName, StringComparison.Ordinal)
                    || String.Equals(branch.Name, "heads/" + shortName, StringComparison.Ordinal)
                    || String.Equals(branch.Name, "refs/heads/" + shortName, StringComparison.Ordinal))
                {
                    found = branch;
                }
            }

            if (found == null)
            {
                List<string> names = new List<string>();
                foreach (BranchInfo branch in branches)
                    names.Add(branch.Name);
                AssertTrue(false, "local branch " + shortName + " is missing from [" + String.Join(", ", names) + "]");
            }

            return found!;
        }

        private static void AssertUnavailableDivergence(BranchInfo branch, string reason)
        {
            AssertEqual(0, branch.Ahead, reason + " leaves the ahead count at zero");
            AssertEqual(0, branch.Behind, reason + " leaves the behind count at zero");
            string? text = ReadDivergenceError(branch);
            AssertTrue(!String.IsNullOrWhiteSpace(text), reason + " should serialize an explicit divergence error");
        }

        private static void AssertUnavailableDivergenceAbsent(BranchInfo branch)
        {
            string? text = ReadDivergenceError(branch);
            AssertTrue(String.IsNullOrWhiteSpace(text), "a measured branch serializes no divergence error");
        }

        private static string? ReadDivergenceError(BranchInfo branch)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            JsonNode? node = JsonSerializer.SerializeToNode(branch, options);
            JsonNode? error = node == null ? null : node["divergenceError"];
            if (error is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                return value.GetValue<string>();
            return null;
        }

        private sealed class BranchListCommandExecutor : IHostCommandExecutor
        {
            private readonly string? _Counts;

            public BranchListCommandExecutor(string? counts)
            {
                _Counts = counts;
            }

            /// <summary>
            /// Return a fixed branch list, then a scripted rev-list result.
            /// </summary>
            /// <param name="request">Command to run.</param>
            /// <param name="token">Cancellation token.</param>
            /// <returns>The scripted command result.</returns>
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                if (request.Arguments.Count > 0 && request.Arguments[0] == "for-each-ref")
                {
                    string separator = "\x1f";
                    return Task.FromResult(new HostCommandResult { StandardOutput = "topic" + separator + "" + separator + "abc123" + separator + "2026-01-01T00:00:00+00:00" + separator + "topic\n" });
                }

                if (request.Arguments.Count > 0 && request.Arguments[0] == "rev-list")
                {
                    if (_Counts == null)
                        return Task.FromResult(new HostCommandResult { ExitCode = 128, StandardError = "missing ref" });
                    return Task.FromResult(new HostCommandResult { StandardOutput = _Counts });
                }

                throw new InvalidOperationException("Unexpected git command in branch listing test.");
            }
        }

        private static async Task<string> CommitOnNewBranchAsync(string repo, string branch, string startCommit, string file, string content)
        {
            await RunGitAsync(repo, "checkout", "-q", "-b", branch, startCommit).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(repo, file), content).ConfigureAwait(false);
            await RunGitAsync(repo, "add", "-A").ConfigureAwait(false);
            await RunGitAsync(repo, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "-m", branch).ConfigureAwait(false);
            return (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
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
            startInfo.Environment["LC_ALL"] = "C";

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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.GitService",
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
                suiteId: "Services.GitService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
