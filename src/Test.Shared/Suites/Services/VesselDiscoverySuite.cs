namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the vessel discovery engine: nested repositories, worktrees, Armada-managed directories, depth
    /// and exclude rules, duplicate paths in different case, spaces and unicode, ../ normalization, missing paths,
    /// allowed-root enforcement, git remote and default-branch inference, existing-vessel matching, browsing, and a
    /// 1,000-directory performance bound.
    /// </summary>
    public sealed class VesselDiscoverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselDiscovery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the vessel discovery suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("nested_repositories_stop_at_first", "A repository nested inside another is not reported", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string outer = MakeFakeRepo(Path.Combine(root, "outer"));
                MakeFakeRepo(Path.Combine(outer, "inner"));

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                AssertEqual(1, result.Candidates.Count, "candidate count");
                AssertEqual(Norm(outer), result.Candidates[0].Path);
                AssertEqual(VesselImportCandidateStatusEnum.New, result.Candidates[0].Status);
            }));

            cases.Add(CaseAsync("worktree_reported_as_worktree", "A git worktree is reported with status Worktree", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string repo = Path.Combine(root, "main-repo");
                Directory.Move(TestGitRepoHelper.CreateWorkingRepoCopy(), repo);
                string worktree = Path.Combine(root, "feature-wt");
                RunGit(repo, "worktree", "add", "-b", "feature", worktree);

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                AssertEqual(2, result.Candidates.Count, "candidate count");
                VesselImportCandidate wt = Find(result, worktree);
                AssertEqual(VesselImportCandidateStatusEnum.Worktree, wt.Status);
                AssertEqual("feature", wt.DefaultBranch, "worktree current branch");
                AssertEqual(VesselImportCandidateStatusEnum.New, Find(result, repo).Status);
            }));

            cases.Add(CaseAsync("armada_directories_are_managed", "Armada's repos, docks, and data directories are reported as ArmadaManaged", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                ArmadaSettings settings = NewSettings(root);
                settings.ReposDirectory = Path.Combine(root, "armada-repos");
                settings.DocksDirectory = Path.Combine(root, "armada-docks");
                MakeFakeRepo(Path.Combine(settings.ReposDirectory, "bare-one"));
                MakeFakeRepo(Path.Combine(settings.DocksDirectory, "dock-one"));
                MakeFakeRepo(Path.Combine(root, "user-repo"));

                VesselDiscoveryResult result = await Discover(testDb, root, settings, new[] { root }).ConfigureAwait(false);
                AssertEqual(VesselImportCandidateStatusEnum.ArmadaManaged, Find(result, settings.ReposDirectory).Status);
                AssertEqual(VesselImportCandidateStatusEnum.ArmadaManaged, Find(result, settings.DocksDirectory).Status);
                AssertEqual(VesselImportCandidateStatusEnum.New, Find(result, Path.Combine(root, "user-repo")).Status);
                AssertEqual(3, result.Candidates.Count, "nothing inside managed directories");

                VesselDiscoveryResult direct = await Discover(testDb, root, settings, null, new[] { Path.Combine(settings.DocksDirectory, "dock-one") }).ConfigureAwait(false);
                AssertEqual(VesselImportCandidateStatusEnum.ArmadaManaged, direct.Candidates.Single().Status, "explicit path inside docks");
            }));

            cases.Add(CaseAsync("depth_limit_applies", "Repositories deeper than MaxDepth are not found", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string deep = MakeFakeRepo(Path.Combine(root, "l1", "l2", "l3", "deep-repo"));

                VesselDiscoveryRequest shallow = new VesselDiscoveryRequest();
                shallow.Roots = new List<string> { root };
                shallow.MaxDepth = 3;
                VesselDiscoveryResult shallowResult = await NewService(testDb, NewSettings(root)).DiscoverAsync(Constants.DefaultTenantId, shallow).ConfigureAwait(false);
                AssertEqual(1, shallowResult.Candidates.Count);
                AssertEqual(VesselImportCandidateStatusEnum.NotGit, shallowResult.Candidates[0].Status, "root reported NotGit");

                shallow.MaxDepth = 4;
                VesselDiscoveryResult deepResult = await NewService(testDb, NewSettings(root)).DiscoverAsync(Constants.DefaultTenantId, shallow).ConfigureAwait(false);
                AssertEqual(Norm(deep), deepResult.Candidates.Single().Path);
            }));

            cases.Add(CaseAsync("exclude_list_and_dot_directories_skipped", "Excluded names and dot-prefixed directories are not scanned", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                MakeFakeRepo(Path.Combine(root, "node_modules", "pkg"));
                MakeFakeRepo(Path.Combine(root, ".hidden", "secret"));
                MakeFakeRepo(Path.Combine(root, "bin", "x"));
                string kept = MakeFakeRepo(Path.Combine(root, "kept"));

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                AssertEqual(1, result.Candidates.Count);
                AssertEqual(Norm(kept), result.Candidates[0].Path);
            }));

            cases.Add(CaseAsync("duplicate_paths_in_different_case", "Duplicate paths differing only in case are de-duplicated on case-insensitive platforms", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string repo = MakeFakeRepo(Path.Combine(root, "MixedCase"));
                string lower = Path.Combine(root, "mixedcase");

                VesselDiscoveryResult result = await Discover(testDb, root, null, null, new[] { repo, lower, repo + Path.DirectorySeparatorChar }).ConfigureAwait(false);
                if (OperatingSystem.IsLinux())
                {
                    AssertEqual(2, result.Candidates.Count, "Linux is case-sensitive");
                    AssertEqual(VesselImportCandidateStatusEnum.NotFound, Find(result, lower).Status);
                }
                else
                {
                    AssertEqual(1, result.Candidates.Count, "case-insensitive de-duplication");
                    AssertTrue(result.Candidates[0].Path.EndsWith("MixedCase", StringComparison.Ordinal), "on-disk casing repaired: " + result.Candidates[0].Path);
                }
            }));

            cases.Add(CaseAsync("spaces_and_unicode", "Paths with spaces and unicode are discovered", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string repo = MakeFakeRepo(Path.Combine(root, "my repo \u00fc\u65e5\u672c"));

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                AssertEqual(1, result.Candidates.Count);
                AssertEqual(Norm(repo), result.Candidates[0].Path);
                AssertEqual("my repo \u00fc\u65e5\u672c", result.Candidates[0].ProposedName.Normalize());
            }));

            cases.Add(CaseAsync("dotdot_path_normalized", "A ../ path is normalized", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string repo = MakeFakeRepo(Path.Combine(root, "target"));
                Directory.CreateDirectory(Path.Combine(root, "other"));
                string dotted = Path.Combine(root, "other") + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "target";

                VesselDiscoveryResult result = await Discover(testDb, root, null, null, new[] { dotted }).ConfigureAwait(false);
                AssertEqual(Norm(repo), result.Candidates.Single().Path);
            }));

            cases.Add(CaseAsync("missing_path_not_found_with_hint", "A nonexistent path is NotFound and yields the PathNotVisibleToAdmiral hint", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string missing = Path.Combine(root, "does-not-exist");

                VesselDiscoveryResult result = await Discover(testDb, root, null, null, new[] { missing }).ConfigureAwait(false);
                AssertEqual(VesselImportCandidateStatusEnum.NotFound, result.Candidates.Single().Status);
                AssertTrue(result.Hints.Any(h => h.Code == VesselImportCodes.PathNotVisibleToAdmiral), "hint present");

                string present = MakeFakeRepo(Path.Combine(root, "present"));
                VesselDiscoveryResult mixed = await Discover(testDb, root, null, null, new[] { missing, present }).ConfigureAwait(false);
                AssertFalse(mixed.Hints.Any(h => h.Code == VesselImportCodes.PathNotVisibleToAdmiral), "no hint when some paths exist");
            }));

            cases.Add(CaseAsync("paths_outside_allowed_roots_rejected", "Paths outside the allowed roots, including ../ escapes, are rejected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string allowed = Path.Combine(root, "allowed");
                Directory.CreateDirectory(allowed);
                MakeFakeRepo(Path.Combine(root, "outside"));
                VesselDiscoveryService service = NewService(testDb, NewSettings(allowed));

                VesselDiscoveryRequest escape = new VesselDiscoveryRequest();
                escape.Directories = new List<string> { allowed + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "outside" };
                await AssertThrowsAsync<VesselImportPathNotAllowedException>(() => service.DiscoverAsync(Constants.DefaultTenantId, escape), "../ escape").ConfigureAwait(false);
                await AssertThrowsAsync<VesselImportPathNotAllowedException>(() => service.BrowseAsync(root), "browse outside").ConfigureAwait(false);

                VesselDiscoveryRequest relative = new VesselDiscoveryRequest();
                relative.Directories = new List<string> { "relative/path" };
                await AssertThrowsAsync<ArgumentException>(() => service.DiscoverAsync(Constants.DefaultTenantId, relative), "relative path").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.DiscoverAsync(Constants.DefaultTenantId, new VesselDiscoveryRequest()), "empty request").ConfigureAwait(false);

                VesselDiscoveryRequest harbor = new VesselDiscoveryRequest();
                harbor.Roots = new List<string> { allowed };
                harbor.HarborId = "hbr_x";
                await AssertThrowsAsync<NotSupportedException>(() => service.DiscoverAsync(Constants.DefaultTenantId, harbor), "harbor").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("remote_and_default_branch_inferred", "Origin URL and default branch are inferred from git", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string bare = TestGitRepoHelper.CreateBareRepoCopy();
                string withRemote = Path.Combine(root, "with-remote");
                Directory.Move(TestGitRepoHelper.CreateWorkingRepoCopy(), withRemote);
                RunGit(withRemote, "remote", "add", "origin", bare);
                RunGit(withRemote, "fetch", "origin");
                RunGit(withRemote, "remote", "set-head", "origin", "main");
                RunGit(withRemote, "checkout", "-b", "topic");

                string noRemote = Path.Combine(root, "no-remote");
                Directory.Move(TestGitRepoHelper.CreateWorkingRepoCopy(), noRemote);
                RunGit(noRemote, "checkout", "-b", "develop");

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                VesselImportCandidate remote = Find(result, withRemote);
                AssertEqual(bare, remote.RemoteUrl, "remote url");
                AssertEqual("main", remote.DefaultBranch, "origin HEAD wins over current branch");
                VesselImportCandidate local = Find(result, noRemote);
                AssertNull(local.RemoteUrl, "no remote");
                AssertEqual("develop", local.DefaultBranch, "current branch fallback");
            }));

            cases.Add(CaseAsync("existing_vessels_matched_and_names_suffixed", "Existing vessels are matched by working directory or remote URL, and names are made unique", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                string byDirectory = MakeFakeRepo(Path.Combine(root, "by-dir"));
                string collide = MakeFakeRepo(Path.Combine(root, "group-a", "app"));
                string collide2 = MakeFakeRepo(Path.Combine(root, "group-b", "app"));

                Vessel existing = new Vessel();
                existing.TenantId = Constants.DefaultTenantId;
                existing.Name = "already-here";
                existing.RepoUrl = "https://example.com/x/by-dir.git";
                existing.WorkingDirectory = byDirectory + Path.DirectorySeparatorChar;
                existing = await testDb.Driver.Vessels.CreateAsync(existing).ConfigureAwait(false);

                Vessel nameHolder = new Vessel();
                nameHolder.TenantId = Constants.DefaultTenantId;
                nameHolder.Name = "app";
                nameHolder.RepoUrl = "https://example.com/elsewhere.git";
                await testDb.Driver.Vessels.CreateAsync(nameHolder).ConfigureAwait(false);

                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                VesselImportCandidate matched = Find(result, byDirectory);
                AssertEqual(VesselImportCandidateStatusEnum.AlreadyOnboarded, matched.Status);
                AssertEqual(existing.Id, matched.ExistingVesselId);
                AssertEqual("already-here", matched.ProposedName);

                List<string> names = new List<string> { Find(result, collide).ProposedName, Find(result, collide2).ProposedName };
                names.Sort(StringComparer.Ordinal);
                AssertEqual("app-2", names[0]);
                AssertEqual("app-3", names[1]);
            }));

            cases.Add(CaseAsync("repo_url_normalization", "Repository URLs normalize across scheme, credentials, case, and .git suffix", TestTags.Positive, () =>
            {
                string? a = VesselImportPaths.NormalizeRepoUrl("https://user@GitHub.com/Owner/Repo.git/");
                string? b = VesselImportPaths.NormalizeRepoUrl("git@github.com:owner/repo.git");
                string? c = VesselImportPaths.NormalizeRepoUrl("ssh://git@github.com:22/owner/repo");
                AssertEqual(a, b);
                AssertEqual(a, c);
                AssertNotEqual(a, VesselImportPaths.NormalizeRepoUrl("https://github.com/owner/other"));
                AssertNull(VesselImportPaths.NormalizeRepoUrl("  "));
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("browse_lists_flags", "Browse lists subdirectories with repository, worktree, and subdirectory flags", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                MakeFakeRepo(Path.Combine(root, "b-repo"));
                Directory.CreateDirectory(Path.Combine(root, "a-folder", "child"));
                string wt = Path.Combine(root, "c-worktree");
                Directory.CreateDirectory(wt);
                File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: /nowhere\n");
                Directory.CreateDirectory(Path.Combine(root, ".dot"));
                Directory.CreateDirectory(Path.Combine(root, "node_modules"));

                VesselDiscoveryService service = NewService(testDb, NewSettings(root));
                VesselBrowseResult listing = await service.BrowseAsync(root).ConfigureAwait(false);
                AssertEqual(3, listing.Entries.Count, "dot and excluded names skipped");
                AssertEqual("a-folder", listing.Entries[0].Name);
                AssertTrue(listing.Entries[0].HasSubdirectories, "a-folder has subdirectories");
                AssertTrue(listing.Entries[1].IsGitRepository, "b-repo is a repository");
                AssertTrue(listing.Entries[2].IsWorktree, "c-worktree is a worktree");
                AssertNull(listing.Parent, "root has no browsable parent");

                VesselBrowseResult roots = await service.BrowseAsync(null).ConfigureAwait(false);
                AssertEqual(1, roots.Entries.Count, "allowed roots listed");
                AssertEqual(Norm(root), roots.Entries[0].Path);

                VesselBrowseResult child = await service.BrowseAsync(Path.Combine(root, "a-folder")).ConfigureAwait(false);
                AssertEqual(Norm(root), child.Parent);
            }));

            cases.Add(CaseAsync("thousand_empty_directories_under_five_seconds", "A root with 1,000 empty directories is scanned in under 5 seconds", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                for (int i = 0; i < 1000; i++) Directory.CreateDirectory(Path.Combine(root, "d" + (i / 50), "e" + i));

                Stopwatch sw = Stopwatch.StartNew();
                VesselDiscoveryResult result = await Discover(testDb, root, null, new[] { root }).ConfigureAwait(false);
                sw.Stop();
                AssertEqual(VesselImportCandidateStatusEnum.NotGit, result.Candidates.Single().Status);
                AssertTrue(sw.Elapsed.TotalSeconds < 5.0, "elapsed " + sw.Elapsed.TotalSeconds + " s");
            }));

            cases.Add(CaseAsync("candidate_cap_truncates", "Discovery stops at MaxCandidates and reports truncation", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                string root = TestTemp.NewDirectory("discover");
                for (int i = 0; i < 5; i++) MakeFakeRepo(Path.Combine(root, "r" + i));
                VesselDiscoveryService service = NewService(testDb, NewSettings(root));
                service.MaxCandidates = 3;
                VesselDiscoveryRequest request = new VesselDiscoveryRequest();
                request.Roots = new List<string> { root };
                VesselDiscoveryResult result = await service.DiscoverAsync(Constants.DefaultTenantId, request).ConfigureAwait(false);
                AssertEqual(3, result.Candidates.Count);
                AssertTrue(result.Truncated, "truncated");
                AssertTrue(result.Hints.Any(h => h.Code == VesselImportCodes.CandidateLimitReached), "hint");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Discovery",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings NewSettings(string allowedRoot)
        {
            ArmadaSettings settings = new ArmadaSettings();
            string data = TestTemp.NewDirectory("discover_data");
            settings.DataDirectory = data;
            settings.ReposDirectory = Path.Combine(data, "repos");
            settings.DocksDirectory = Path.Combine(data, "docks");
            settings.Import.AllowedRoots = new List<string> { allowedRoot };
            return settings;
        }

        private static VesselDiscoveryService NewService(TestDatabase testDb, ArmadaSettings settings)
        {
            return new VesselDiscoveryService(testDb.Driver, settings);
        }

        private static Task<VesselDiscoveryResult> Discover(TestDatabase testDb, string allowedRoot, ArmadaSettings? settings, string[]? roots, string[]? directories = null)
        {
            VesselDiscoveryRequest request = new VesselDiscoveryRequest();
            request.Roots = roots == null ? new List<string>() : roots.ToList();
            request.Directories = directories == null ? new List<string>() : directories.ToList();
            return NewService(testDb, settings ?? NewSettings(allowedRoot)).DiscoverAsync(Constants.DefaultTenantId, request);
        }

        private static string MakeFakeRepo(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, ".git"));
            return path;
        }

        private static string Norm(string path)
        {
            return VesselImportPaths.NormalizeInputPath(path);
        }

        private static VesselImportCandidate Find(VesselDiscoveryResult result, string path)
        {
            string normalized = Norm(path);
            VesselImportCandidate? candidate = result.Candidates.FirstOrDefault(c => String.Equals(c.Path, normalized, VesselImportPaths.PathComparison));
            if (candidate == null)
                throw new AssertionException("no candidate for " + normalized + "; got " + String.Join(", ", result.Candidates.Select(c => c.Path + "=" + c.Status)));
            return candidate;
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
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
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);

            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed: " + standardError.Trim());
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
