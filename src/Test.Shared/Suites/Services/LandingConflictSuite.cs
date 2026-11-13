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
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for merge-conflict capture in the landing path: the real-git
    /// <see cref="GitService.GetConflictedFilesAsync"/> utility and its use by
    /// <see cref="LandingService.RetryLandingAsync"/> to record the conflicting file list on the
    /// mission when a retry fails. Positive cases assert conflict detection and capture; negative cases
    /// assert clean-tree emptiness and missing-mission handling.
    /// </summary>
    public sealed class LandingConflictSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingConflict";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Landing Conflict suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (bool throwingResolver in new[] { false, true })
            {
                cases.Add(CaseAsync("remote_conflict_routes_and_aborts_" + throwingResolver, "Remote conflict routes resolver and abort through dock host", TestTags.Negative, async () =>
                {
                    RecordingConflictExecutor executor = new RecordingConflictExecutor(throwingResolver);
                    GitService git = new GitService(CreateLogging(), executor);
                    GitCommitMergeResult result = await git.MergeCommitAsync("/nonlocal/dock", "abc123", null, "landing");
                    AssertEqual(GitCommitMergeOutcomeEnum.Conflicted, result.Outcome);
                    AssertContains("conflict.txt", String.Join(",", result.ConflictedFiles));
                    AssertTrue(executor.ConflictReads >= 2, "Both listing and resolver must use the host executor");
                    AssertTrue(executor.Aborted, "Conflict must attempt merge --abort even when resolver throws");
                    if (throwingResolver) AssertContains("resolver transport failure", result.Message ?? "");
                }));
            }

            foreach (string dockPath in new[] { "/nonlocal/dock", "C:\\docks\\dock" })
            {
                bool windowsDock = dockPath.StartsWith("C:");
                cases.Add(CaseAsync("remote_changelog_conflict_uses_dock_shell_" + (windowsDock ? "windows" : "posix"), "Remote CHANGELOG conflict resolves with the dock host shell", TestTags.Positive, async () =>
                {
                    RecordingChangelogExecutor executor = new RecordingChangelogExecutor(dockPath);
                    GitService git = new GitService(CreateLogging(), executor);
                    GitCommitMergeResult result = await git.MergeCommitAsync(dockPath, "abc123", null, "landing");
                    AssertEqual(GitCommitMergeOutcomeEnum.Merged, result.Outcome);
                    AssertTrue(executor.Shells.Count > 0, "Resolver shell must run through the dock host executor");
                    foreach (string shell in executor.Shells) AssertEqual(windowsDock ? "cmd.exe" : "/bin/sh", shell);
                    AssertFalse(executor.Aborted, "A resolved CHANGELOG conflict must not be aborted");
                }));
            }

            cases.Add(CaseAsync("retry_landing_missing_dock_reports_reason", "RetryLanding on a missing dock folder reports dock missing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    git.ExistingBranches.Add("feature/gone");
                    Vessel vessel = new Vessel("Missing Dock Vessel", "https://github.com/test/missingdock");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_landing_" + Guid.NewGuid().ToString("N"));
                    await db.Vessels.CreateAsync(vessel);
                    Dock dock = new Dock(vessel.Id)
                    {
                        BranchName = "feature/gone",
                        WorktreePath = Path.Combine(Path.GetTempPath(), "armada_missing_dock_" + Guid.NewGuid().ToString("N"))
                    };
                    await db.Docks.CreateAsync(dock);
                    Mission mission = new Mission("Dock vanished")
                    {
                        Status = MissionStatusEnum.LandingFailed,
                        VesselId = vessel.Id,
                        DockId = dock.Id,
                        BranchName = "feature/gone"
                    };
                    await db.Missions.CreateAsync(mission);

                    LandingService landing = new LandingService(CreateLogging(), db, CreateSettings(), git);
                    bool landingRan = false;
                    landing.OnPerformLanding = (m, d) => { landingRan = true; return Task.CompletedTask; };

                    AssertFalse(await landing.RetryLandingAsync(mission.Id), "Retry must fail when the dock folder is gone");
                    AssertFalse(landingRan, "Landing must not run against a missing worktree");
                    Mission reread = (await db.Missions.ReadAsync(mission.Id))!;
                    AssertEqual(MissionStatusEnum.LandingFailed, reread.Status);
                    AssertContains("Dock missing", reread.FailureReason ?? "");
                    AssertContains(dock.WorktreePath!, reread.FailureReason ?? "");
                    AssertContains("feature/gone", reread.FailureReason ?? "");
                }
            }));

            cases.Add(CaseAsync("retry_landing_harbor_dock_not_checked_locally", "RetryLanding never falls back to the Admiral for a disconnected Harbor dock", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    git.ExistingBranches.Add("feature/harbor");
                    Vessel vessel = new Vessel("Harbor Dock Vessel", "https://github.com/test/harbordock");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_landing_" + Guid.NewGuid().ToString("N"));
                    await db.Vessels.CreateAsync(vessel);
                    Dock dock = new Dock(vessel.Id)
                    {
                        BranchName = "feature/harbor",
                        HarborId = "hbr_remote",
                        RepositoryPath = "C:\\harbor\\repo.git",
                        WorktreePath = "C:\\harbor\\docks\\dock"
                    };
                    await db.Docks.CreateAsync(dock);
                    Mission mission = new Mission("Harbor landing")
                    {
                        Status = MissionStatusEnum.LandingFailed,
                        VesselId = vessel.Id,
                        DockId = dock.Id,
                        BranchName = "feature/harbor"
                    };
                    await db.Missions.CreateAsync(mission);

                    LandingService landing = new LandingService(CreateLogging(), db, CreateSettings(), git);
                    Dock? landedDock = null;
                    landing.OnPerformLanding = async (m, d) =>
                    {
                        landedDock = d;
                        m.Status = MissionStatusEnum.Complete;
                        await db.Missions.UpdateAsync(m);
                    };

                    AssertFalse(await landing.RetryLandingAsync(mission.Id), "No local-host fallback is permitted");
                    AssertNull(landedDock);
                    AssertContains("Harbor host is unavailable", (await db.Missions.ReadAsync(mission.Id))!.FailureReason!);
                }
            }));

            cases.Add(CaseAsync("git_detects_conflicted_files", "Git DetectsConflictedFiles", TestTags.Positive, async () =>
            {
                string dir = CreateTempDir();
                try
                {
                    Git(dir, "init", "-b", "main");
                    Git(dir, "config", "user.email", "test@armada.local");
                    Git(dir, "config", "user.name", "Armada Test");
                    string file = Path.Combine(dir, "conflict.txt");
                    await File.WriteAllTextAsync(file, "base\n");
                    Git(dir, "add", "conflict.txt");
                    Git(dir, "commit", "-m", "base");

                    Git(dir, "checkout", "-b", "feature");
                    await File.WriteAllTextAsync(file, "feature change\n");
                    Git(dir, "commit", "-am", "feature");

                    Git(dir, "checkout", "main");
                    await File.WriteAllTextAsync(file, "main change\n");
                    Git(dir, "commit", "-am", "main");

                    Git(dir, "merge", "feature"); // conflicts, leaves unmerged path

                    GitService git = new GitService(CreateLogging());
                    IReadOnlyList<string> conflicts = await git.GetConflictedFilesAsync(dir);
                    AssertTrue(conflicts.Count >= 1, "Expected at least one conflicted file");
                    AssertContains("conflict.txt", string.Join(",", conflicts));
                }
                finally
                {
                    TryDelete(dir);
                }
            }));

            cases.Add(CaseAsync("git_clean_tree_has_no_conflicts", "Git CleanTree NoConflicts", TestTags.Negative, async () =>
            {
                string dir = TestGitRepoHelper.CreateWorkingRepoCopy();
                try
                {
                    GitService git = new GitService(CreateLogging());
                    IReadOnlyList<string> conflicts = await git.GetConflictedFilesAsync(dir);
                    AssertEqual(0, conflicts.Count);
                }
                finally
                {
                    TryDelete(dir);
                }
            }));

            cases.Add(CaseAsync("retry_landing_captures_conflicts", "RetryLanding CapturesConflicts", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    git.ExistingBranches.Add("feature/x");
                    git.ConflictedFilesResult = new List<string> { "src/Foo.cs", "src/Bar.cs" };

                    Vessel vessel = new Vessel("Conflict Vessel", "https://github.com/test/conflict");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_landing_" + Guid.NewGuid().ToString("N"));
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("Retry me")
                    {
                        Status = MissionStatusEnum.LandingFailed,
                        VesselId = vessel.Id,
                        BranchName = "feature/x"
                    };
                    await db.Missions.CreateAsync(mission);

                    LandingService landing = new LandingService(CreateLogging(), db, CreateSettings(), git);
                    landing.OnPerformLanding = (m, d) => throw new InvalidOperationException("merge failed");

                    bool ok = await landing.RetryLandingAsync(mission.Id);
                    AssertFalse(ok, "Retry should report failure");

                    Mission? reread = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(reread);
                    AssertEqual(MissionStatusEnum.LandingFailed, reread!.Status);
                    AssertContains("src/Foo.cs", reread.FailureReason ?? "");
                    AssertContains("src/Bar.cs", reread.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.LandingConflict, reread.FailureKind, "failure kind is recorded where the failure happens");
                }
            }));

            cases.Add(CaseAsync("retry_landing_missing_mission_returns_false", "RetryLanding MissingMission ReturnsFalse", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LandingService landing = new LandingService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    AssertFalse(await landing.RetryLandingAsync("msn_missing"));
                }
            }));

            cases.Add(CaseAsync("retry_landing_lands_review_without_gate", "RetryLanding lands a Review mission that has no review gate", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    git.ExistingBranches.Add("feature/land");

                    Vessel vessel = new Vessel("Review Vessel", "https://github.com/test/review");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_landing_" + Guid.NewGuid().ToString("N"));
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("Land me from review")
                    {
                        Status = MissionStatusEnum.Review,
                        RequiresReview = false,
                        VesselId = vessel.Id,
                        BranchName = "feature/land"
                    };
                    await db.Missions.CreateAsync(mission);

                    LandingService landing = new LandingService(CreateLogging(), db, CreateSettings(), git);
                    landing.OnPerformLanding = async (m, d) =>
                    {
                        m.Status = MissionStatusEnum.Complete;
                        m.CompletedUtc = DateTime.UtcNow;
                        await db.Missions.UpdateAsync(m);
                    };

                    bool ok = await landing.RetryLandingAsync(mission.Id);
                    AssertTrue(ok, "A Review mission with requiresReview=false should be landable");

                    Mission? reread = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(reread);
                    AssertEqual(MissionStatusEnum.Complete, reread!.Status);
                }
            }));

            cases.Add(CaseAsync("retry_landing_refuses_review_gate", "RetryLanding refuses a review-gated mission (must go through Approve/Deny)", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    Vessel vessel = new Vessel("Gated Vessel", "https://github.com/test/gated");
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("Needs explicit review")
                    {
                        Status = MissionStatusEnum.Review,
                        RequiresReview = true,
                        VesselId = vessel.Id,
                        BranchName = "feature/y"
                    };
                    await db.Missions.CreateAsync(mission);

                    LandingService landing = new LandingService(CreateLogging(), db, CreateSettings(), new StubGitService());
                    landing.OnPerformLanding = (m, d) => throw new InvalidOperationException("landing must not run for a review-gated mission");

                    bool ok = await landing.RetryLandingAsync(mission.Id);
                    AssertFalse(ok, "A review-gated mission must not be landable via retry");

                    Mission? reread = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionStatusEnum.Review, reread!.Status);
                }
            }));

            cases.Add(CaseAsync("classifier_only_conflicts_are_landing_conflicts", "Only a merge conflict git reports classifies as LandingConflict; process-start and other failures are Infra", TestTags.Positive, async () =>
            {
                AssertEqual(MissionFailureKindEnum.LandingConflict, LandingFailureClassifier.Classify(new GitMergeConflictException("conflict", new[] { "a.txt" })));
                AssertEqual(MissionFailureKindEnum.LandingConflict, LandingFailureClassifier.Classify(new InvalidOperationException("wrapped", new GitMergeConflictException("conflict"))));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new Win32Exception(2, "An error occurred trying to start process '/usr/bin/git'. No such file or directory")));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new DirectoryNotFoundException("gone")));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new IOException("io")));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new InvalidOperationException("An error occurred trying to start process 'git' with working directory '/x'.")));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new GitDirtyCheckoutException("/w", " M a.txt")));
                AssertEqual(MissionFailureKindEnum.Infra, LandingFailureClassifier.Classify(new InvalidOperationException("push rejected")));
                await Task.CompletedTask;
            }));

            cases.Add(CaseAsync("landing_dock_missing_unrepairable_is_infra", "Landing with a vanished dock whose branch is gone fails as Infra naming the path and branch, and is not rescued", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                StubGitService git = new StubGitService();
                MissionLandingHandler handler = CreateHandler(db, git);
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("Gone Dock", "https://github.com/test/gone")
                {
                    LocalPath = Path.Combine(Path.GetTempPath(), "armada_gone_bare_" + Guid.NewGuid().ToString("N")),
                    WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_gone_work_" + Guid.NewGuid().ToString("N")),
                    LandingMode = LandingModeEnum.MergeAndPush
                });
                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id)
                {
                    Active = true,
                    BranchName = "armada/gone/branch",
                    WorktreePath = Path.Combine(Path.GetTempPath(), "armada_gone_dock_" + Guid.NewGuid().ToString("N"))
                });
                Mission mission = await db.Missions.CreateAsync(new Mission("Gone dock")
                {
                    Status = MissionStatusEnum.WorkProduced, VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName, DiffSnapshot = "diff"
                });

                await handler.HandleMissionCompleteAsync(mission, dock);

                Mission failed = (await db.Missions.ReadAsync(mission.Id))!;
                AssertEqual(MissionStatusEnum.LandingFailed, failed.Status);
                AssertEqual(MissionFailureKindEnum.Infra, failed.FailureKind, "a missing dock is not a landing conflict");
                AssertContains("Dock missing", failed.FailureReason ?? "");
                AssertContains(dock.WorktreePath!, failed.FailureReason ?? "");
                AssertContains(dock.BranchName!, failed.FailureReason ?? "");
                AssertFalse(MissionFailureClassifier.IsRecoverable(MissionFailureClassifier.Classify(failed)), "no autonomous rescue re-implements it");
                AssertEqual(0, git.PushCalls.Count, "nothing published");
            }));

            cases.Add(CaseAsync("landing_dock_missing_recreated_and_lands", "Landing with a vanished dock recreates the worktree from the mission branch and lands the same commit", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                Directory.Delete(f.Dock.WorktreePath!, true);

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                Mission landed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                AssertEqual(f.WorkCommit, f.Repos.OriginHead("main"), "the mission's own commit is on origin");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(50);
                AssertTrue(events.Exists(e => e.EventType == "dock.repaired" && e.EntityId == f.Dock.Id), "the repair is recorded");
            }));

            cases.Add(CaseAsync("landing_git_cannot_start_is_infra", "Git failing to start during landing is Infra, not a landing conflict", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                DatabaseDriver db = testDb.Driver;
                GitService git = new GitService(CreateLogging(), new ProcessStartFailureExecutor());
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = CreateSettings();
                MissionLandingHandler handler = new MissionLandingHandler(logging, db, settings, git, new RecordingMergeQueueService(),
                    new MessageTemplateService(logging), null, new DockService(logging, db, settings, git), null);
                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("No Git", "https://github.com/test/nogit")
                {
                    LocalPath = TestTemp.NewDirectory("nogit_bare"),
                    WorkingDirectory = TestTemp.NewDirectory("nogit_work"),
                    LandingMode = LandingModeEnum.MergeAndPush
                });
                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { Active = true, BranchName = "armada/nogit", WorktreePath = TestTemp.NewDirectory("nogit_dock") });
                Mission mission = await db.Missions.CreateAsync(new Mission("No git")
                {
                    Status = MissionStatusEnum.WorkProduced, VesselId = vessel.Id, DockId = dock.Id, BranchName = dock.BranchName, DiffSnapshot = "diff"
                });

                await handler.HandleMissionCompleteAsync(mission, dock);

                Mission failed = (await db.Missions.ReadAsync(mission.Id))!;
                AssertEqual(MissionStatusEnum.LandingFailed, failed.Status, failed.FailureReason);
                AssertEqual(MissionFailureKindEnum.Infra, failed.FailureKind, "a process that cannot start is an environment failure");
                AssertContains("trying to start process", failed.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("landing_real_conflict_is_landing_conflict", "A real merge conflict with the current target is recorded as LandingConflict and the dock is left clean", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush, "shared.txt", "mission version\n");
                f.Repos.AdvanceMain("shared.txt", "target version\n");
                string target = f.Repos.OriginHead("main");

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                Mission failed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.LandingFailed, failed.Status);
                AssertEqual(MissionFailureKindEnum.LandingConflict, failed.FailureKind, failed.FailureReason);
                AssertContains("shared.txt", failed.FailureReason ?? "");
                AssertEqual(target, f.Repos.OriginHead("main"), "nothing was published");
                AssertTrue((await f.Git.GetWorkingTreeStatusAsync(f.Dock.WorktreePath!)).IsClean, "the conflicted merge was aborted");
            }));

            cases.Add(CaseAsync("retry_landing_reads_conflicts_from_merge_worktree", "RetryLanding lists conflicted files from the dock worktree where the merge ran, not from the owning repository", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush, "shared.txt", "mission version\n");
                f.Repos.AdvanceMain("shared.txt", "target version\n");
                await MarkLandingFailedAsync(testDb.Driver, f.Mission);

                LandingService landing = new LandingService(f.Logging, testDb.Driver, f.Settings, f.Git);
                landing.OnPerformLanding = async (m, d) =>
                {
                    // A landing that dies mid-merge leaves the conflict in the dock worktree.
                    await HarborDockGitFixture.TryGitAsync(d.WorktreePath!, "fetch", "-q", "origin");
                    await HarborDockGitFixture.TryGitAsync(d.WorktreePath!, "-c", "user.name=Armada Test", "-c", "user.email=test@armada.local", "merge", "--no-edit", "origin/main");
                    throw new InvalidOperationException("landing handler stopped mid-merge");
                };

                AssertFalse(await landing.RetryLandingAsync(f.Mission.Id), "the retry fails");
                Mission failed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.LandingFailed, failed.Status);
                AssertEqual(MissionFailureKindEnum.LandingConflict, failed.FailureKind, failed.FailureReason);
                AssertContains("merge conflicts in: shared.txt", failed.FailureReason ?? "");
                AssertEqual(0, (await f.Git.GetConflictedFilesAsync(f.Vessel.LocalPath!)).Count, "the owning repository holds no conflict, so reading it found nothing");
            }));

            cases.Add(CaseAsync("retry_landing_uses_conflicts_carried_by_aborted_merge", "RetryLanding records the conflicted files a merge read before it aborted, even though the worktree is clean again", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                await MarkLandingFailedAsync(testDb.Driver, f.Mission);

                LandingService landing = new LandingService(f.Logging, testDb.Driver, f.Settings, f.Git);
                landing.OnPerformLanding = (m, d) => throw new InvalidOperationException("wrapped", new GitMergeConflictException("conflict", new[] { "src/A.cs", "src/B.cs" }));

                AssertFalse(await landing.RetryLandingAsync(f.Mission.Id));
                Mission failed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionFailureKindEnum.LandingConflict, failed.FailureKind);
                AssertContains("merge conflicts in: src/A.cs, src/B.cs", failed.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("retry_landing_non_conflict_failure_lists_no_files", "RetryLanding of a failure that is not a merge conflict records Infra and no conflicted files", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                await MarkLandingFailedAsync(testDb.Driver, f.Mission);

                LandingService landing = new LandingService(f.Logging, testDb.Driver, f.Settings, f.Git);
                landing.OnPerformLanding = (m, d) => throw new InvalidOperationException("push rejected");

                AssertFalse(await landing.RetryLandingAsync(f.Mission.Id));
                Mission failed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionFailureKindEnum.Infra, failed.FailureKind);
                AssertEqual("Landing retry failed: push rejected", failed.FailureReason);
            }));

            cases.Add(CaseAsync("landing_target_advanced_event_records_before_and_after", "Integrating a moved target records mission.landing_target_advanced with the dock head before and after and the target tip", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                f.Repos.AdvanceMain("other.txt", "target moved\n");
                string targetTip = f.Repos.OriginHead("main");

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                Mission landed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(100);
                ArmadaEvent? advanced = events.Find(e => e.EventType == "mission.landing_target_advanced" && e.EntityId == f.Mission.Id);
                AssertNotNull(advanced, "the advance is recorded");
                AssertNotNull(advanced!.Payload, "the event carries a payload");
                LandingTargetAdvancedPayload payload = System.Text.Json.JsonSerializer.Deserialize<LandingTargetAdvancedPayload>(advanced.Payload!)!;
                AssertEqual("main", payload.TargetBranch);
                AssertEqual(f.WorkCommit, payload.Before, "Before is the mission's own commit");
                AssertEqual(landed.CommitHash, payload.After, "After is the integrated commit landing verified and published");
                AssertEqual(targetTip, payload.RemoteTargetTip, "the target tip integrated against");
                AssertTrue(payload.Before != payload.After, "the head moved");
            }));

            cases.Add(CaseAsync("landing_target_unchanged_records_no_advance", "Landing onto a target that has not moved records no mission.landing_target_advanced event", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!.Status);
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(100);
                AssertFalse(events.Exists(e => e.EventType == "mission.landing_target_advanced" && e.EntityId == f.Mission.Id), "nothing was integrated");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Landing Conflict",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task MarkLandingFailedAsync(DatabaseDriver db, Mission mission)
        {
            mission.Status = MissionStatusEnum.LandingFailed;
            mission.FailureKind = MissionFailureKindEnum.Infra;
            mission.FailureReason = "previous landing failed";
            await db.Missions.UpdateAsync(mission);
        }

        private static MissionLandingHandler CreateHandler(DatabaseDriver db, StubGitService git)
        {
            LoggingModule logging = CreateLogging();
            ArmadaSettings settings = CreateSettings();
            return new MissionLandingHandler(logging, db, settings, git, new RecordingMergeQueueService(),
                new MessageTemplateService(logging), null, new DockService(logging, db, settings, git), null);
        }

        private sealed class ProcessStartFailureExecutor : IHostCommandExecutor
        {
            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                throw new Win32Exception(2, "An error occurred trying to start process '" + request.Executable + "' with working directory '"
                    + request.WorkingDirectory + "'. No such file or directory");
            }
        }

        private sealed class RecordingChangelogExecutor : IHostCommandExecutor
        {
            private readonly string _Dock;
            public List<string> Shells { get; } = new List<string>();
            public bool Aborted { get; private set; }

            public RecordingChangelogExecutor(string dock) { _Dock = dock; }

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                AssertEqual(_Dock, request.WorkingDirectory);
                HostCommandResult result = new HostCommandResult();
                if (request.Executable != "git")
                {
                    Shells.Add(request.Executable);
                    return Task.FromResult(result);
                }
                string args = String.Join(" ", request.Arguments);
                if (args.StartsWith("merge-base")) result.ExitCode = 1;
                else if (args.StartsWith("merge --no-ff"))
                {
                    result.ExitCode = 1;
                    result.StandardError = "Merge conflict in CHANGELOG.md";
                }
                else if (args.StartsWith("diff --name-only")) result.StandardOutput = args.Contains("-z") ? "CHANGELOG.md\0" : "CHANGELOG.md\n";
                else if (args == "merge --abort") Aborted = true;
                return Task.FromResult(result);
            }
        }

        private sealed class RecordingConflictExecutor : IHostCommandExecutor
        {
            private readonly bool _Throw;
            public int ConflictReads { get; private set; }
            public bool Aborted { get; private set; }

            public RecordingConflictExecutor(bool throwingResolver) { _Throw = throwingResolver; }

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                AssertEqual("/nonlocal/dock", request.WorkingDirectory);
                string args = String.Join(" ", request.Arguments);
                HostCommandResult result = new HostCommandResult();
                if (args.StartsWith("merge-base")) result.ExitCode = 1;
                else if (args.StartsWith("merge --no-ff"))
                {
                    result.ExitCode = 1;
                    result.StandardError = "Merge conflict in conflict.txt";
                }
                else if (args.StartsWith("diff --name-only"))
                {
                    ConflictReads++;
                    if (_Throw && ConflictReads == 2) throw new InvalidOperationException("resolver transport failure");
                    result.StandardOutput = args.Contains("-z") ? "conflict.txt\0" : "conflict.txt\n";
                }
                else if (args == "merge --abort") Aborted = true;
                return Task.FromResult(result);
            }
        }

        private static void Git(string dir, params string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            using Process process = Process.Start(psi)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(30000);
            Task.WaitAll(new Task[] { stdout, stderr }, 5000);
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada_conflict_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
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
