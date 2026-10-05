namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
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
                    // TODO(R5): no typed exception exists for a dirty checkout; the Armada-owned message is the only signal.
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
                    AssertTrue(ex!.Message.Contains("contains tracked modifications", StringComparison.Ordinal), "Dirty landing checkout should be rejected with a clear error");
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
