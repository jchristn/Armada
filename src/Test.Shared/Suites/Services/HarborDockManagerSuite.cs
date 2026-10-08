namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Harbor;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Harbor side of Harbor-hosted docks, against real git repositories in temporary folders: a vessel resolves to
    /// the checkout named in the Harbor's settings, to a checkout discovered under a root folder by its remote URL, or to
    /// the Harbor's own clone; docks are worktrees under the Harbor's docks folder; creating one never changes the
    /// user's working tree, index, or current branch; reclaiming one removes and prunes it; file requests stay inside the
    /// docks folder.
    /// </summary>
    public sealed class HarborDockManagerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborDockManager";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("resolve_prefers_mapped_checkout", "A vessel named in the Harbor's settings resolves to that checkout", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.Repositories.Add(new HarborRepositoryMapping("APP", git.Checkout));
                HarborDockManager manager = NewManager(git);

                HarborDockResult result = await manager.HandleAsync(Request(HarborDockOperationEnum.Resolve, "https://example.com/other/repo.git")).ConfigureAwait(false);

                AssertTrue(result.Success, result.Message);
                AssertEqual(HarborRepositorySourceEnum.Mapped, result.Source);
                AssertEqual(git.Checkout, result.CheckoutPath);
                AssertEqual(git.Checkout, result.RepositoryPath);
            }));

            cases.Add(CaseAsync("discovery_matches_ssh_remote_for_https_url", "A checkout under a root folder whose ssh remote names the vessel's https URL is discovered", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(git.Checkout, "remote", "add", "upstream", "git@github.com:Example/App.git").ConfigureAwait(false);
                string unrelated = Path.Combine(git.CodeRoot, "nested", "other");
                Directory.CreateDirectory(unrelated);
                await HarborDockGitFixture.GitAsync(unrelated, "init").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(unrelated, "remote", "add", "origin", "https://github.com/example/other.git").ConfigureAwait(false);
                git.Settings.RepositoryRoots.Add(git.CodeRoot);
                HarborDockManager manager = NewManager(git);

                HarborDockResult result = await manager.HandleAsync(Request(HarborDockOperationEnum.Resolve, "https://github.com/example/app")).ConfigureAwait(false);

                AssertTrue(result.Success, result.Message);
                AssertEqual(HarborRepositorySourceEnum.Discovered, result.Source);
                AssertEqual(PathCanonicalizer.Canonicalize(git.Checkout), PathCanonicalizer.Canonicalize(result.CheckoutPath!));
            }));

            cases.Add(CaseAsync("resolve_without_checkout_or_url_fails_with_reason", "With no checkout and no repository URL the Harbor cannot serve the vessel and says what to set", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.RepositoryRoots.Add(git.CodeRoot);
                HarborDockManager manager = NewManager(git);

                HarborDockResult result = await manager.HandleAsync(Request(HarborDockOperationEnum.Resolve, null)).ConfigureAwait(false);

                AssertFalse(result.Success, "no checkout and nothing to clone");
                AssertEqual(HarborRepositorySourceEnum.None, result.Source);
                AssertContains("app", result.Message ?? String.Empty, "the message names the vessel");
                AssertContains("Harbor > Settings > Repositories", result.Message ?? String.Empty, "the message names the setting");
            }));

            cases.Add(CaseAsync("resolve_without_checkout_uses_clone", "Without a checkout the vessel resolves to the Harbor's own clone of its URL", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                HarborDockManager manager = NewManager(git);

                HarborDockResult result = await manager.HandleAsync(Request(HarborDockOperationEnum.Resolve, "https://github.com/example/app.git")).ConfigureAwait(false);

                AssertTrue(result.Success, result.Message);
                AssertEqual(HarborRepositorySourceEnum.Clone, result.Source);
                AssertNull(result.CheckoutPath, "no checkout");
                AssertEqual(Path.Combine(git.HarborRepos, "app.git"), result.RepositoryPath);
            }));

            cases.Add(CaseAsync("provision_from_checkout_leaves_user_tree_untouched", "A dock from the user's checkout is a worktree in the docks folder; the checkout's files, index, and branch are unchanged", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));

                // The user is mid-work on a feature branch: a staged change and an unstaged change.
                await HarborDockGitFixture.GitAsync(git.Checkout, "checkout", "-b", "feature/mine").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "staged.txt"), "staged\n").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(git.Checkout, "add", "staged.txt").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "README.md"), "# app, edited by the user\n").ConfigureAwait(false);
                string statusBefore = await HarborDockGitFixture.GitAsync(git.Checkout, "status", "--porcelain=v1", "-z").ConfigureAwait(false);
                string indexBefore = await HarborDockGitFixture.GitAsync(git.Checkout, "diff", "--cached", "--name-only", "-z").ConfigureAwait(false);
                string headBefore = await HarborDockGitFixture.GitAsync(git.Checkout, "symbolic-ref", "HEAD").ConfigureAwait(false);
                string mainCommit = (await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/main").ConfigureAwait(false))!;

                HarborDockManager manager = NewManager(git);
                HarborDockResult result = await manager.HandleAsync(Provision("https://github.com/example/app.git", "armada/mission-1", "msn_one")).ConfigureAwait(false);

                AssertTrue(result.Success, result.Message);
                AssertEqual(HarborRepositorySourceEnum.Mapped, result.Source);
                AssertEqual(Path.Combine(git.HarborDocks, "app", "msn_one"), result.WorktreePath);
                AssertTrue(Directory.Exists(result.WorktreePath!), "the dock exists on the Harbor");
                AssertEqual(mainCommit, result.HeadCommit, "the mission branch starts at origin's main");
                AssertEqual("refs/heads/armada/mission-1", await HarborDockGitFixture.GitAsync(result.WorktreePath!, "symbolic-ref", "HEAD").ConfigureAwait(false));
                AssertEqual(mainCommit, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/armada/mission-1").ConfigureAwait(false), "the mission branch is in the user's repository");
                AssertFalse((await HarborDockGitFixture.TryGitAsync(git.Checkout, "config", "--get", "branch.armada/mission-1.remote").ConfigureAwait(false)).Success, "no upstream is configured for the mission branch");

                AssertEqual(statusBefore, await HarborDockGitFixture.GitAsync(git.Checkout, "status", "--porcelain=v1", "-z").ConfigureAwait(false), "the working tree is unchanged");
                AssertEqual(indexBefore, await HarborDockGitFixture.GitAsync(git.Checkout, "diff", "--cached", "--name-only", "-z").ConfigureAwait(false), "the index is unchanged");
                AssertEqual(headBefore, await HarborDockGitFixture.GitAsync(git.Checkout, "symbolic-ref", "HEAD").ConfigureAwait(false), "the current branch is unchanged");
                AssertEqual("# app, edited by the user\n", await File.ReadAllTextAsync(Path.Combine(git.Checkout, "README.md")).ConfigureAwait(false), "the user's edit is still there");
            }));

            cases.Add(CaseAsync("provision_reuses_existing_mission_branch", "A later stage on the same branch gets a dock on that branch, with its earlier commits", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                HarborDockManager manager = NewManager(git);

                HarborDockResult first = await manager.HandleAsync(Provision(null, "armada/stage", "msn_a")).ConfigureAwait(false);
                AssertTrue(first.Success, first.Message);
                string stageCommit = await HarborDockGitFixture.CommitFileAsync(first.WorktreePath!, "stage.txt", "one\n", "stage one").ConfigureAwait(false);
                AssertTrue((await manager.HandleAsync(Reclaim(first)).ConfigureAwait(false)).Success, "first dock reclaimed");

                HarborDockResult second = await manager.HandleAsync(Provision(null, "armada/stage", "msn_b")).ConfigureAwait(false);
                AssertTrue(second.Success, second.Message);
                AssertEqual(stageCommit, second.HeadCommit, "the second dock continues the branch");
            }));

            cases.Add(CaseAsync("provision_from_fallback_clone", "Without a checkout the Harbor clones the vessel's URL into its clones folder and docks from that clone", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                HarborDockManager manager = NewManager(git);
                string mainCommit = (await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/main").ConfigureAwait(false))!;

                HarborDockResult result = await manager.HandleAsync(Provision(git.Origin, "armada/cloned", "msn_clone")).ConfigureAwait(false);

                AssertTrue(result.Success, result.Message);
                AssertEqual(HarborRepositorySourceEnum.Clone, result.Source);
                AssertEqual(Path.Combine(git.HarborRepos, "app.git"), result.RepositoryPath);
                AssertEqual("true", await HarborDockGitFixture.GitAsync(result.RepositoryPath!, "rev-parse", "--is-bare-repository").ConfigureAwait(false), "a bare clone");
                AssertEqual(mainCommit, result.HeadCommit);
                AssertTrue(HarborDockSettings.IsSameOrUnder(result.WorktreePath!, git.HarborDocks), "the dock is in the Harbor's docks folder");

                // A second mission fetches the existing clone instead of cloning again.
                string newer = await HarborDockGitFixture.CommitFileAsync(git.Checkout, "newer.txt", "x\n", "newer").ConfigureAwait(false);
                await HarborDockGitFixture.GitAsync(git.Checkout, "push", "origin", "main").ConfigureAwait(false);
                HarborDockResult again = await manager.HandleAsync(Provision(git.Origin, "armada/cloned-2", "msn_clone_2")).ConfigureAwait(false);
                AssertTrue(again.Success, again.Message);
                AssertEqual(newer, again.HeadCommit, "the clone was fetched");
            }));

            cases.Add(CaseAsync("provision_with_unreachable_url_fails_with_reason", "A clone that cannot be made fails the provision with the reason and the setting to change", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                HarborDockManager manager = NewManager(git);

                HarborDockResult result = await manager.HandleAsync(Provision(Path.Combine(git.Root, "missing.git"), "armada/x", "msn_x")).ConfigureAwait(false);

                AssertFalse(result.Success, "nothing to clone");
                AssertContains("Harbor > Settings > Repositories", result.Message ?? String.Empty);
                AssertFalse(Directory.Exists(Path.Combine(git.HarborRepos, "app.git")), "no partial clone is left");
            }));

            cases.Add(CaseAsync("reclaim_removes_and_prunes_worktree", "Reclaiming a dock removes its folder and its registration in the user's repository", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                HarborDockManager manager = NewManager(git);
                HarborDockResult dock = await manager.HandleAsync(Provision(null, "armada/reclaim", "msn_r")).ConfigureAwait(false);
                AssertTrue(dock.Success, dock.Message);
                AssertTrue((await HarborDockGitFixture.WorktreesAsync(git.Checkout).ConfigureAwait(false)).Contains(PathCanonicalizer.Canonicalize(dock.WorktreePath!)), "registered");

                HarborDockResult reclaimed = await manager.HandleAsync(Reclaim(dock)).ConfigureAwait(false);

                AssertTrue(reclaimed.Success, reclaimed.Message);
                AssertFalse(Directory.Exists(dock.WorktreePath!), "the dock folder is gone");
                AssertFalse((await HarborDockGitFixture.WorktreesAsync(git.Checkout).ConfigureAwait(false)).Contains(PathCanonicalizer.Canonicalize(dock.WorktreePath!)), "no longer registered");
                AssertTrue(File.Exists(Path.Combine(git.Checkout, "README.md")), "the checkout is untouched");
            }));

            cases.Add(CaseAsync("reclaim_refuses_path_outside_docks_folder", "The Harbor never removes a path outside its docks folder", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                HarborDockManager manager = NewManager(git);
                HarborDockRequest request = new HarborDockRequest { RequestId = "r", Operation = HarborDockOperationEnum.Reclaim, VesselName = "app", WorktreePath = git.Checkout, RepositoryPath = git.Checkout };

                HarborDockResult result = await manager.HandleAsync(request).ConfigureAwait(false);

                AssertFalse(result.Success, "refused");
                AssertTrue(Directory.Exists(git.Checkout), "the checkout is still there");
            }));

            cases.Add(CaseAsync("file_requests_stay_inside_docks_folder", "Dock files can be written, read, and excluded from git; paths outside the docks folder are refused", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                git.Settings.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                HarborDockManager manager = NewManager(git);
                HarborDockResult dock = await manager.HandleAsync(Provision(null, "armada/files", "msn_f")).ConfigureAwait(false);
                string file = Path.Combine(dock.WorktreePath!, ".armada", "playbooks", "p.md");

                HarborFileResult write = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "w", Operation = HarborFileOperationEnum.Write, Path = file, Content = "play\n" }).ConfigureAwait(false);
                HarborFileResult read = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "r", Operation = HarborFileOperationEnum.Read, Path = file }).ConfigureAwait(false);
                HarborFileResult stat = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "s", Operation = HarborFileOperationEnum.Stat, Path = dock.WorktreePath! }).ConfigureAwait(false);
                HarborFileResult missing = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "m", Operation = HarborFileOperationEnum.Read, Path = Path.Combine(dock.WorktreePath!, "nope.md") }).ConfigureAwait(false);
                HarborFileResult exclude = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "e", Operation = HarborFileOperationEnum.AddGitExclude, Path = dock.WorktreePath!, Content = "CLAUDE.md" }).ConfigureAwait(false);
                HarborFileResult outside = await manager.HandleFileAsync(new HarborFileRequest { RequestId = "o", Operation = HarborFileOperationEnum.Write, Path = Path.Combine(git.Checkout, "evil.txt"), Content = "x" }).ConfigureAwait(false);

                AssertTrue(write.Success, write.Message);
                AssertTrue(read.Success && read.Exists, read.Message);
                AssertEqual("play\n", read.Content);
                AssertTrue(stat.Exists && stat.IsDirectory, "the dock is a directory");
                AssertTrue(missing.Success && !missing.Exists, "a missing file reads as not there");
                AssertTrue(exclude.Success, exclude.Message);
                string excludeFile = await File.ReadAllTextAsync(Path.Combine(git.Checkout, ".git", "info", "exclude")).ConfigureAwait(false);
                AssertTrue(new List<string>(excludeFile.Split('\n')).Contains("CLAUDE.md"), "the exclude entry is in the repository's shared exclude file");
                AssertFalse(outside.Success, "a path outside the docks folder is refused");
                AssertFalse(File.Exists(Path.Combine(git.Checkout, "evil.txt")), "nothing was written into the checkout");
            }));

            cases.Add(CaseAsync("settings_validation_reports_each_problem", "Repository settings are validated: relative paths, missing or non-git checkouts, duplicates, and a docks folder inside a checkout", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                AssertEqual(0, git.Settings.Validate().Count, "the defaults are valid");

                HarborDockSettings bad = git.Settings.Clone();
                bad.DocksDirectory = Path.Combine(git.Checkout, "docks");
                bad.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                bad.Repositories.Add(new HarborRepositoryMapping("APP", git.Checkout));
                bad.Repositories.Add(new HarborRepositoryMapping("plain", git.CodeRoot));
                bad.Repositories.Add(new HarborRepositoryMapping("", git.Checkout));
                bad.Repositories.Add(new HarborRepositoryMapping("rel", "relative/path"));
                bad.RepositoryRoots.Add(Path.Combine(git.Root, "no-such-root"));

                List<string> errors = bad.Validate();
                AssertEqual(7, errors.Count, "one message per problem: " + String.Join(" | ", errors));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor Dock Manager", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static HarborDockManager NewManager(HarborDockGitFixture git)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new HarborDockManager(() => git.Settings, logging);
        }

        private static HarborDockRequest Request(HarborDockOperationEnum operation, string? repoUrl)
        {
            return new HarborDockRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = operation,
                VesselId = "vsl_app",
                VesselName = "app",
                RepoUrl = repoUrl,
                DefaultBranch = "main"
            };
        }

        private static HarborDockRequest Provision(string? repoUrl, string branch, string dockName)
        {
            HarborDockRequest request = Request(HarborDockOperationEnum.Provision, repoUrl);
            request.BranchName = branch;
            request.DockName = dockName;
            return request;
        }

        private static HarborDockRequest Reclaim(HarborDockResult dock)
        {
            return new HarborDockRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = HarborDockOperationEnum.Reclaim,
                VesselName = "app",
                WorktreePath = dock.WorktreePath,
                RepositoryPath = dock.RepositoryPath
            };
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
