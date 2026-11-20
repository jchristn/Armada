namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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
    /// Descriptors for <see cref="NoOpCompletionDetector"/> and the completion gate that calls it.
    /// Positive cases confirm a fast, empty-diff, trivial-output completion is flagged. Negative cases
    /// confirm real work, slow runs, unknown duration, and verbose output are not flagged. Dock cases
    /// run <see cref="MissionService.HandleCompletionAsync"/> against a real git worktree.
    /// </summary>
    public sealed class NoOpCompletionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.NoOpCompletion";
        private const string StaleBranch = "feature/stale";
        private const string CompletionOutput = NoOpCompletionDetector.CompletionMarker + "\nDone.";
        private const string SeededBranchDiff = "diff --git a/notes.txt b/notes.txt\n+prior\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the no-op completion suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("noop_empty_diff_fast_trivial_is_flagged", "Empty diff + fast + trivial output is flagged", TestTags.Positive, () =>
            {
                AssertTrue(NoOpCompletionDetector.IsNoOp("", "Done.", 1200));
                AssertTrue(NoOpCompletionDetector.IsNoOp(null, "   ", 500));
                AssertTrue(NoOpCompletionDetector.IsNoOp("   \n  ", "[ARMADA:PROGRESS] 100", 3000));
            }));

            cases.Add(Case("noop_nonempty_diff_not_flagged", "Non-empty diff is never flagged", TestTags.Negative, () =>
            {
                AssertFalse(NoOpCompletionDetector.IsNoOp("diff --git a/x b/x\n+change", "Done.", 1000));
            }));

            cases.Add(Case("noop_slow_run_not_flagged", "A slow run is not a fast false-complete", TestTags.Negative, () =>
            {
                // Empty diff but the captain spent real time investigating -> legitimate "nothing to do".
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "Investigated; no change needed.", 120000));
            }));

            cases.Add(Case("noop_unknown_runtime_not_flagged", "Unknown runtime is not flagged (conservative)", TestTags.Negative, () =>
            {
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "Done.", null));
            }));

            cases.Add(Case("noop_verbose_output_not_flagged", "Verbose output is not trivial", TestTags.Negative, () =>
            {
                string verbose = new string('x', 1000);
                AssertFalse(NoOpCompletionDetector.IsNoOp("", verbose, 2000));
            }));

            cases.Add(Case("noop_custom_thresholds_respected", "Custom thresholds are honored", TestTags.Positive, () =>
            {
                // With a 10s cap, a 5s run is fast; with a 2s cap, the same run is not.
                AssertTrue(NoOpCompletionDetector.IsNoOp("", "ok", 5000, maxRuntimeMs: 10000));
                AssertFalse(NoOpCompletionDetector.IsNoOp("", "ok", 5000, maxRuntimeMs: 2000));
            }));

            cases.Add(CaseAsync("stale_branch_diff_without_dock_changes_is_noop", "A default-branch diff with no files changed since dock start is a no-op", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase()).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertEqual(MissionStatusEnum.Failed, stored.Status);
                AssertEqual(MissionFailureKindEnum.NoOp, stored.FailureKind);
                AssertContains("No-op completion", stored.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("stale_branch_diff_without_marker_is_unfinished", "No files since dock start and no completion line is unfinished work", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase
                {
                    AgentOutput = new string('x', 1000),
                    RuntimeMs = 120000
                }).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertEqual(MissionStatusEnum.Failed, stored.Status);
                AssertEqual(MissionFailureKindEnum.NoOp, stored.FailureKind);
                AssertContains("Unfinished completion", stored.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("new_commit_since_dock_start_is_not_noop", "A commit after dock start is not a no-op", TestTags.Positive, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { CommitAfterStart = true }).ConfigureAwait(false);
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("tracked_edit_since_dock_start_is_not_noop", "A tracked edit after dock start is not a no-op", TestTags.Positive, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { EditAfterStart = true }).ConfigureAwait(false);
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("untracked_file_since_dock_start_is_not_noop", "An untracked file after dock start is not a no-op", TestTags.Positive, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { UntrackedAfterStart = true }).ConfigureAwait(false);
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("slow_completion_without_dock_changes_is_not_noop", "A slow completion with no new files keeps the runtime exemption", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { RuntimeMs = 120000 }).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("missing_dock_start_with_branch_diff_is_not_rejected", "A missing dock start commit does not reject a branch diff", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { RecordStart = false }).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("missing_dock_start_without_diff_is_not_rejected", "A missing dock start commit does not reject an empty diff", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase
                {
                    RecordStart = false,
                    CaptureBranchDiff = false
                }).ConfigureAwait(false);
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("removed_worktree_with_branch_diff_is_not_rejected", "A Git error does not turn a branch diff into a no-op", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase
                {
                    RemoveWorktree = true,
                    CaptureBranchDiff = false,
                    SeededDiff = SeededBranchDiff
                }).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("removed_worktree_without_diff_is_not_rejected", "A Git error is not treated as an empty file list", TestTags.Negative, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase
                {
                    RemoveWorktree = true,
                    CaptureBranchDiff = false
                }).ConfigureAwait(false);
                AssertAccepted(stored);
            }));

            cases.Add(CaseAsync("audit_without_dock_changes_completes", "An Audit mission with no new files still completes", TestTags.Positive, async () =>
            {
                Mission stored = await CompleteStaleBranchAsync(new StaleBranchCase { Mode = MissionModeEnum.Audit }).ConfigureAwait(false);
                AssertContains("notes.txt", stored.DiffSnapshot ?? "");
                AssertEqual(MissionStatusEnum.Complete, stored.Status);
                AssertNull(stored.FailureKind);
                AssertEqual(0, stored.NoOpRedispatchAttempts);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "No-Op Completion Detector",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void AssertAccepted(Mission mission)
        {
            AssertEqual(MissionStatusEnum.WorkProduced, mission.Status);
            AssertNull(mission.FailureKind);
            AssertEqual(0, mission.NoOpRedispatchAttempts);
        }

        private static async Task<Mission> CompleteStaleBranchAsync(StaleBranchCase scenario)
        {
            string root = TestTemp.NewDirectory("noop-completion");
            string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
            try
            {
                await RunGitAsync(repo, "checkout", "-q", "-b", StaleBranch).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(repo, "notes.txt"), "prior\n").ConfigureAwait(false);
                await RunGitAsync(repo, "add", "notes.txt").ConfigureAwait(false);
                await RunGitAsync(repo, "commit", "-q", "-m", "prior work").ConfigureAwait(false);
                string start = (await RunGitAsync(repo, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                if (scenario.CommitAfterStart)
                {
                    await File.WriteAllTextAsync(Path.Combine(repo, "added.txt"), "added\n").ConfigureAwait(false);
                    await RunGitAsync(repo, "add", "added.txt").ConfigureAwait(false);
                    await RunGitAsync(repo, "commit", "-q", "-m", "new work").ConfigureAwait(false);
                }

                if (scenario.EditAfterStart)
                {
                    await File.WriteAllTextAsync(Path.Combine(repo, "notes.txt"), "edited\n").ConfigureAwait(false);
                }

                if (scenario.UntrackedAfterStart)
                {
                    await File.WriteAllTextAsync(Path.Combine(repo, "extra.txt"), "extra\n").ConfigureAwait(false);
                }

                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.LogDirectory = Path.Combine(root, "logs");
                    settings.DocksDirectory = Path.Combine(root, "docks");
                    settings.ReposDirectory = Path.Combine(root, "repos");
                    settings.MaxNoOpRedispatchAttempts = 0;
                    Directory.CreateDirectory(settings.LogDirectory);
                    Directory.CreateDirectory(settings.DocksDirectory);
                    Directory.CreateDirectory(settings.ReposDirectory);

                    GitService git = new GitService(logging);
                    DockService docks = new DockService(logging, db, settings, git);
                    CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                    MissionService missions = new MissionService(logging, db, settings, docks, captains, git: git);
                    if (scenario.CaptureBranchDiff)
                    {
                        missions.OnCaptureDiff = async (mission, dock) =>
                        {
                            string diff = await git.DiffAsync(dock.WorktreePath!, "main").ConfigureAwait(false);
                            mission.DiffSnapshot = diff;
                            await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        };
                    }

                    Fleet fleet = new Fleet("Example");
                    await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                    Vessel vessel = new Vessel("Example", "https://example.invalid/repo.git");
                    vessel.FleetId = fleet.Id;
                    vessel.DefaultBranch = "main";
                    await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("captain");
                    captain.State = CaptainStateEnum.Working;
                    await db.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.CaptainId = captain.Id;
                    dock.BranchName = StaleBranch;
                    dock.WorktreePath = repo;
                    dock.Active = true;
                    await db.Docks.CreateAsync(dock).ConfigureAwait(false);

                    if (scenario.RecordStart)
                    {
                        string startDirectory = Path.Combine(settings.LogDirectory, "docks");
                        Directory.CreateDirectory(startDirectory);
                        await File.WriteAllTextAsync(Path.Combine(startDirectory, dock.Id + ".start"), start + "\n").ConfigureAwait(false);
                    }

                    if (scenario.RemoveWorktree)
                    {
                        TestTemp.TryDelete(repo);
                    }

                    Mission mission = new Mission("Example");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.Mode = scenario.Mode;
                    mission.Persona = PersonaCatalog.Worker;
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.DockId = dock.Id;
                    mission.BranchName = StaleBranch;
                    mission.CommitHash = start;
                    mission.TotalRuntimeMs = scenario.RuntimeMs;
                    mission.AgentOutput = scenario.AgentOutput;
                    mission.DiffSnapshot = scenario.SeededDiff;
                    await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    await missions.HandleCompletionAsync(captain, mission.Id).ConfigureAwait(false);
                    Mission? stored = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertNotNull(stored);
                    return stored!;
                }
            }
            finally
            {
                TestTemp.TryDelete(repo);
                TestTemp.TryDelete(root);
            }
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
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using (Process process = new Process { StartInfo = startInfo })
            {
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

        #region Private-Types

        private sealed class StaleBranchCase
        {
            public bool RecordStart { get; set; } = true;

            public bool RemoveWorktree { get; set; }

            public bool CommitAfterStart { get; set; }

            public bool EditAfterStart { get; set; }

            public bool UntrackedAfterStart { get; set; }

            public bool CaptureBranchDiff { get; set; } = true;

            public string? SeededDiff { get; set; }

            public string AgentOutput { get; set; } = CompletionOutput;

            public long RuntimeMs { get; set; } = 1000;

            public MissionModeEnum Mode { get; set; } = MissionModeEnum.Implementation;
        }

        #endregion
    }
}
