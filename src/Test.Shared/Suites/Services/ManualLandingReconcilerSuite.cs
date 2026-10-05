namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// F12: a WorkProduced mission on a Landing Mode None vessel moves to Complete once its branch is merged into the
    /// target branch by hand (detected with git merge-base --is-ancestor on a real repository), and only then.
    /// </summary>
    public sealed class ManualLandingReconcilerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ManualLandingReconciler";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("merged_branch_completes_mission", "A manual-landing mission completes once its branch is merged into main, not before", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                    try
                    {
                        CommitOnBranch(repo, "armada/manual-work");
                        Mission mission = await CreateMissionAsync(testDb, repo, LandingModeEnum.None, "armada/manual-work");
                        ManualLandingReconciler reconciler = CreateReconciler(testDb);
                        int reconciledEvents = 0;
                        reconciler.OnMissionReconciled = m => { reconciledEvents++; return Task.CompletedTask; };

                        AssertEqual(0, await reconciler.ReconcileAsync(), "not merged yet");
                        Mission? before = await testDb.Driver.Missions.ReadAsync(mission.Id);
                        AssertEqual(MissionStatusEnum.WorkProduced, before!.Status, "still WorkProduced before the merge");

                        RunGit(repo, "merge", "--no-ff", "--no-edit", "armada/manual-work");

                        AssertEqual(1, await reconciler.ReconcileAsync(), "reconciled after the merge");
                        Mission? after = await testDb.Driver.Missions.ReadAsync(mission.Id);
                        AssertEqual(MissionStatusEnum.Complete, after!.Status, "Complete after the merge");
                        AssertNotNull(after.CompletedUtc, "completion time set");
                        AssertEqual(1, reconciledEvents, "callback raised once");

                        AssertEqual(0, await reconciler.ReconcileAsync(), "idempotent");
                    }
                    finally
                    {
                        TestTemp.TryDelete(repo);
                    }
                }
            }));

            cases.Add(CaseAsync("automatic_landing_mode_not_reconciled", "A merged branch on a Local Merge vessel is left to the landing pipeline", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                    try
                    {
                        CommitOnBranch(repo, "armada/auto-work");
                        RunGit(repo, "merge", "--no-ff", "--no-edit", "armada/auto-work");
                        Mission mission = await CreateMissionAsync(testDb, repo, LandingModeEnum.LocalMerge, "armada/auto-work");
                        ManualLandingReconciler reconciler = CreateReconciler(testDb);

                        AssertEqual(0, await reconciler.ReconcileAsync(), "not a manual-landing mission");
                        Mission? after = await testDb.Driver.Missions.ReadAsync(mission.Id);
                        AssertEqual(MissionStatusEnum.WorkProduced, after!.Status, "unchanged");
                    }
                    finally
                    {
                        TestTemp.TryDelete(repo);
                    }
                }
            }));

            cases.Add(CaseAsync("missing_branch_not_reconciled", "A mission whose branch does not exist is not completed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    string repo = TestGitRepoHelper.CreateWorkingRepoCopy();
                    try
                    {
                        Mission mission = await CreateMissionAsync(testDb, repo, LandingModeEnum.None, "armada/never-created");
                        ManualLandingReconciler reconciler = CreateReconciler(testDb);
                        AssertFalse(await reconciler.ReconcileMissionAsync(mission), "unresolvable branch is not merged");
                    }
                    finally
                    {
                        TestTemp.TryDelete(repo);
                    }
                }
            }));

            return new TestSuiteDescriptor(SuiteId, "Manual landing reconciler", cases);
        }

        #endregion

        #region Private-Methods

        private static ManualLandingReconciler CreateReconciler(TestDatabase testDb)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new ManualLandingReconciler(testDb.Driver, new ArmadaSettings(), new GitService(logging), logging);
        }

        private static async Task<Mission> CreateMissionAsync(TestDatabase testDb, string repo, LandingModeEnum mode, string branch)
        {
            Vessel vessel = new Vessel("manual-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://github.com/test/repo.git");
            vessel.LocalPath = repo;
            vessel.DefaultBranch = "main";
            vessel.LandingMode = mode;
            vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Mission mission = new Mission("manual landing", "work");
            mission.VesselId = vessel.Id;
            mission.BranchName = branch;
            mission.Status = MissionStatusEnum.WorkProduced;
            return await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static void CommitOnBranch(string repo, string branch)
        {
            RunGit(repo, "checkout", "-b", branch);
            File.WriteAllText(Path.Combine(repo, "work.txt"), "mission work\n");
            RunGit(repo, "add", "work.txt");
            RunGit(repo, "commit", "-m", "Mission work");
            RunGit(repo, "checkout", "main");
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

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
                process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed (exit " + process.ExitCode + "): " + standardError.Trim());
            }
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
