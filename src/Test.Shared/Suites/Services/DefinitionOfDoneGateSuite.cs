namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="DefinitionOfDoneGate"/>, exercising the real shell executor with portable
    /// <c>exit</c> commands. Positive cases confirm a disabled/unconfigured gate is skipped and a clean
    /// build+test passes; negative cases confirm a failed build classifies Compile, a failed test classifies
    /// TestFail, and a missing checkout classifies Infra. A completion case confirms that cancelling the
    /// mission stops the gate command and leaves the mission Cancelled, and that a real gate failure still
    /// records Failed.
    /// </summary>
    public sealed class DefinitionOfDoneGateSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Definition-of-Done gate suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("disabled_gate_is_skipped", "A disabled gate is skipped and passes", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = false;
                    vessel.DefinitionOfDoneBuildCommand = "exit 1";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertTrue(result.Passed, "disabled gate should pass");
                    AssertTrue(result.Skipped, "disabled gate should be marked skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("enabled_but_no_commands_is_skipped", "An enabled gate with no commands is skipped", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertTrue(result.Passed, "no-command gate should pass");
                    AssertTrue(result.Skipped, "no-command gate should be marked skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("clean_build_and_test_passes", "A clean build and test passes the gate", TestTags.Positive, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 0";
                    vessel.DefinitionOfDoneTestCommand = "exit 0";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.Pass, result.Outcome, "clean build+test should pass");
                    AssertFalse(result.Skipped, "a real run is not skipped");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("failing_build_is_compile", "A failing build classifies as Compile", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 1";
                    vessel.DefinitionOfDoneTestCommand = "exit 0";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.Compile, result.Outcome, "a failing build should classify Compile");
                    AssertFalse(result.Passed, "a failing build should not pass");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("failing_test_is_testfail", "A failing test after a clean build classifies as TestFail", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                string dir = TestTemp.NewDirectory("dod");
                try
                {
                    Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                    vessel.DefinitionOfDoneEnabled = true;
                    vessel.DefinitionOfDoneBuildCommand = "exit 0";
                    vessel.DefinitionOfDoneTestCommand = "exit 3";
                    DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, dir).ConfigureAwait(false);
                    AssertEqual(DefinitionOfDoneOutcomeEnum.TestFail, result.Outcome, "a failing test should classify TestFail");
                }
                finally { SafeDelete(dir); }
            }));

            cases.Add(CaseAsync("missing_checkout_is_infra", "A missing checkout classifies as Infra", TestTags.Negative, async () =>
            {
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
                Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                vessel.DefinitionOfDoneEnabled = true;
                vessel.DefinitionOfDoneBuildCommand = "exit 0";
                string missing = Path.Combine(Path.GetTempPath(), "armada_dod_missing_" + Guid.NewGuid().ToString("N"));
                DefinitionOfDoneResult result = await gate.EvaluateAsync(vessel, missing).ConfigureAwait(false);
                AssertEqual(DefinitionOfDoneOutcomeEnum.Infra, result.Outcome, "a missing checkout should classify Infra");
            }));

            cases.Add(CaseAsync("cancelled_mission_stops_gate_command", "A cancelled mission stops its definition-of-done command", TestTags.Negative, async () =>
            {
                GateRun run = await StartCompletionAsync(false).ConfigureAwait(false);
                using (run.Database)
                {
                    try
                    {
                        bool started = await WaitForLengthAsync(run.BeatPath, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                        AssertTrue(started, "the gate command starts and writes output");

                        await run.Admiral.CancelMissionAsync(run.Mission).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        long before = File.Exists(run.BeatPath) ? new FileInfo(run.BeatPath).Length : 0;
                        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                        bool removed = !File.Exists(run.BeatPath);
                        long after = removed ? before : new FileInfo(run.BeatPath).Length;
                        AssertTrue(removed || (before > 0 && after == before),
                            "the gate command stops after the mission is cancelled (bytes " + before + " then " + after + ")");
                    }
                    finally
                    {
                        await FinishCompletionAsync(run.Completion).ConfigureAwait(false);
                        DeleteTemps(run.TempPaths);
                    }

                    Mission? stored = await run.Database.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "the mission is still stored");
                    AssertEqual(MissionStatusEnum.Cancelled, stored!.Status, "the mission stays Cancelled");
                }
            }));

            cases.Add(CaseAsync("gate_failure_without_cancel_records_failed", "A definition-of-done failure without cancellation records Failed", TestTags.Negative, async () =>
            {
                GateRun run = await StartCompletionAsync(true).ConfigureAwait(false);
                using (run.Database)
                {
                    try
                    {
                        await FinishCompletionAsync(run.Completion).ConfigureAwait(false);
                    }
                    finally
                    {
                        DeleteTemps(run.TempPaths);
                    }

                    Mission? stored = await run.Database.Driver.Missions.ReadAsync(run.Mission.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "the mission is still stored");
                    AssertEqual(MissionStatusEnum.Failed, stored!.Status, "a gate failure records Failed");
                    AssertTrue(stored.FailureReason != null && stored.FailureReason.IndexOf("Definition-of-Done", StringComparison.Ordinal) >= 0,
                        "the failure names the gate");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.DefinitionOfDoneGate",
                displayName: "Definition-of-Done Gate",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static void SafeDelete(string dir)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); }
                catch { }
            }
        }

        private static async Task<GateRun> StartCompletionAsync(bool failImmediately)
        {
            TestDatabase database = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            List<string> temps = new List<string>();
            try
            {
                string worktree = TestTemp.NewDirectory("dod-gate");
                temps.Add(worktree);
                string docks = TestTemp.NewDirectory("dod-docks");
                temps.Add(docks);
                string repos = TestTemp.NewDirectory("dod-repos");
                temps.Add(repos);
                string bare = TestTemp.NewDirectory("dod-bare");
                temps.Add(bare);
                string working = TestTemp.NewDirectory("dod-work");
                temps.Add(working);

                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = new ArmadaSettings();
                settings.DocksDirectory = docks;
                settings.ReposDirectory = repos;
                StubGitService git = new StubGitService();
                IDockService docksService = new DockService(logging, database.Driver, settings, git);
                ICaptainService captains = new CaptainService(logging, database.Driver, settings, git, docksService);
                DefinitionOfDoneGate gate = new DefinitionOfDoneGate(logging);
                MissionService missions = new MissionService(logging, database.Driver, settings, docksService, captains, null, git, gate);
                VoyageService voyages = new VoyageService(logging, database.Driver);
                AdmiralService admiral = new AdmiralService(logging, database.Driver, settings, captains, missions, voyages, docksService);

                Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo.git");
                vessel.LocalPath = bare;
                vessel.WorkingDirectory = working;
                vessel.DefaultBranch = "main";
                vessel.DefinitionOfDoneEnabled = true;
                vessel.DefinitionOfDoneTimeoutSeconds = 30;
                string beatPath = Path.Combine(worktree, "gate-beat");
                vessel.DefinitionOfDoneBuildCommand = failImmediately ? "exit 1" : LongRunningCommand(beatPath);
                await database.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                Captain captain = new Captain("dod-captain");
                captain.State = CaptainStateEnum.Working;
                await database.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                dock.CaptainId = captain.Id;
                dock.WorktreePath = worktree;
                dock.BranchName = "topic/dod-gate";
                dock.Active = true;
                await database.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                Mission mission = new Mission("Definition-of-done gate");
                mission.Status = MissionStatusEnum.InProgress;
                mission.CaptainId = captain.Id;
                mission.DockId = dock.Id;
                mission.VesselId = vessel.Id;
                mission.DiffSnapshot = "diff --git a/file.txt b/file.txt\n+line\n";
                mission.AgentOutput = "Changed the file.\n[ARMADA:RESULT] COMPLETE\n";
                mission.TotalRuntimeMs = 120000;
                await database.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                captain.CurrentMissionId = mission.Id;
                captain.CurrentDockId = dock.Id;
                await database.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                Task completion = missions.HandleCompletionAsync(captain, mission.Id);
                return new GateRun(database, admiral, mission, completion, beatPath, temps);
            }
            catch
            {
                database.Dispose();
                DeleteTemps(temps);
                throw;
            }
        }

        private static string LongRunningCommand(string beatPath)
        {
            if (OperatingSystem.IsWindows())
            {
                string quoted = beatPath.Replace("\"", "");
                return "echo.>>\"" + quoted + "\"& :dodloop& echo.>>\"" + quoted + "\"& ping -n 2 127.0.0.1 >NUL& goto dodloop";
            }

            string escaped = beatPath.Replace("'", "'\\''");
            return "while :; do printf x >> '" + escaped + "'; sleep 0.2; done";
        }

        private static async Task<bool> WaitForLengthAsync(string path, TimeSpan limit)
        {
            DateTime deadline = DateTime.UtcNow.Add(limit);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return false;
        }

        private static async Task FinishCompletionAsync(Task completion)
        {
            try
            {
                await completion.WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("definition-of-done completion did not finish within 45 seconds");
            }
        }

        private static void DeleteTemps(List<string> paths)
        {
            foreach (string path in paths)
                TestTemp.TryDelete(path);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.DefinitionOfDoneGate",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private sealed class GateRun
        {
            public GateRun(TestDatabase database, AdmiralService admiral, Mission mission, Task completion, string beatPath, List<string> tempPaths)
            {
                Database = database;
                Admiral = admiral;
                Mission = mission;
                Completion = completion;
                BeatPath = beatPath;
                TempPaths = tempPaths;
            }

            public TestDatabase Database { get; }

            public AdmiralService Admiral { get; }

            public Mission Mission { get; }

            public Task Completion { get; }

            public string BeatPath { get; }

            public List<string> TempPaths { get; }
        }

        #endregion
    }
}
