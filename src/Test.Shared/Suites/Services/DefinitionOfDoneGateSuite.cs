namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// Descriptors for <see cref="DefinitionOfDoneGate"/>, exercising the real shell executor with portable
    /// <c>exit</c> commands. Positive cases confirm a disabled/unconfigured gate is skipped and a clean
    /// build+test passes; negative cases confirm a failed build classifies Compile, a failed test classifies
    /// TestFail, and a missing checkout classifies Infra. POSIX cases cover caller cancellation, which must stop
    /// the owned process, and a mission cancelled during its completion's gate, which must stop the gate command and
    /// stay Cancelled. Windows skips those cases. A gate failure without a cancel still records Failed.
    /// </summary>
    public sealed class DefinitionOfDoneGateSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string PosixFixtureSkipReason = "This case uses a POSIX shell process fixture, so it does not run on Windows.";

        #endregion

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

            bool skipPosixFixture = OperatingSystem.IsWindows();
            string? posixSkipReason = skipPosixFixture ? PosixFixtureSkipReason : null;
            cases.Add(CaseAsync(
                "caller_cancel_stops_gate_process",
                "Caller cancellation stops the gate command",
                TestTags.Negative,
                CallerCancelStopsGateProcessAsync,
                skipPosixFixture,
                posixSkipReason));

            cases.Add(CaseAsync(
                "cancelled_mission_stops_gate_command",
                "Cancelling a mission stops its definition-of-done command and keeps it Cancelled",
                TestTags.Negative,
                CancelledMissionStopsGateCommandAsync,
                skipPosixFixture,
                posixSkipReason));
            cases.Add(CaseAsync("gate_failure_without_cancel_records_failed", "A definition-of-done failure without a cancel records Failed", TestTags.Negative, GateFailureWithoutCancelRecordsFailedAsync));

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

        private static async Task CallerCancelStopsGateProcessAsync()
        {
            DefinitionOfDoneGate gate = new DefinitionOfDoneGate(CreateLogging());
            string dir = TestTemp.NewDirectory("dod-cancel");
            string pidPath = Path.Combine(dir, "shell.pid");
            int processId = 0;
            try
            {
                Vessel vessel = new Vessel("dod-vessel", "https://github.com/test/repo");
                vessel.DefinitionOfDoneEnabled = true;
                vessel.DefinitionOfDoneTimeoutSeconds = 180;
                vessel.DefinitionOfDoneBuildCommand = "echo $$ > shell.pid; command sleep 90";
                using (CancellationTokenSource cancel = new CancellationTokenSource())
                {
                    Task<DefinitionOfDoneResult> running = gate.EvaluateAsync(vessel, dir, cancel.Token);
                    bool started = await WaitUntilAsync(() => TryReadProcessId(pidPath, out processId), TimeSpan.FromSeconds(40)).ConfigureAwait(false);
                    AssertTrue(started, "the gate process started");
                    cancel.Cancel();
                    Task finished = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                    bool stopped = await WaitUntilAsync(() => !ProcessIsRunning(processId), TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    AssertTrue(finished == running, "the cancelled gate returns");
                    AssertTrue(stopped, "the owned gate process is stopped after cancellation");
                    try
                    {
                        await running.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The gate may surface the caller token after it stops the process.
                    }
                }
            }
            finally
            {
                if (processId > 0) StopProcessTree(processId);
                StopRecordedProcess(pidPath);
                SafeDelete(dir);
            }
        }

        private static async Task CancelledMissionStopsGateCommandAsync()
        {
            string worktree = TestTemp.NewDirectory("dod-cancel-mission");
            string pidPath = Path.Combine(worktree, "shell.pid");
            int processId = 0;
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            try
            {
                CompletionFixture fixture = await CompletionFixture.CreateAsync(testDb.Driver, worktree, "echo $$ > shell.pid; command sleep 90").ConfigureAwait(false);
                Task completion = fixture.Missions.HandleCompletionAsync(fixture.Captain, fixture.Mission.Id);

                bool started = await WaitUntilAsync(() => TryReadProcessId(pidPath, out processId), TimeSpan.FromSeconds(40)).ConfigureAwait(false);
                AssertTrue(started, "the gate command started");
                Mission running = (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                await fixture.Admiral.CancelMissionAsync(running).ConfigureAwait(false);

                Task finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
                AssertTrue(finished == completion, "the completion returns once the mission is cancelled");
                await completion.ConfigureAwait(false);
                bool stopped = await WaitUntilAsync(() => !ProcessIsRunning(processId), TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertTrue(stopped, "the gate command is stopped when its mission is cancelled");

                Mission stored = (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Cancelled, stored.Status, "the mission stays Cancelled; the stopped gate is not a failure");
                AssertFalse((stored.FailureReason ?? String.Empty).Contains("Definition-of-Done", StringComparison.Ordinal), "no gate failure is recorded: " + stored.FailureReason);
                AssertEqual(0, fixture.Landings, "a cancelled mission is not landed");
            }
            finally
            {
                if (processId > 0) StopProcessTree(processId);
                StopRecordedProcess(pidPath);
                SafeDelete(worktree);
            }
        }

        private static async Task GateFailureWithoutCancelRecordsFailedAsync()
        {
            string worktree = TestTemp.NewDirectory("dod-fail-mission");
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            try
            {
                CompletionFixture fixture = await CompletionFixture.CreateAsync(testDb.Driver, worktree, "exit 1").ConfigureAwait(false);
                await fixture.Missions.HandleCompletionAsync(fixture.Captain, fixture.Mission.Id).WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);

                Mission stored = (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Failed, stored.Status, "a gate failure records Failed");
                AssertContains("Definition-of-Done", stored.FailureReason ?? String.Empty, "the failure names the gate");
                AssertEqual(0, fixture.Landings, "a mission that failed its gate is not landed");
            }
            finally
            {
                SafeDelete(worktree);
            }
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(timeout);
            while (!deadline.Passed)
            {
                if (condition()) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return condition();
        }

        private static bool TryReadProcessId(string path, out int processId)
        {
            processId = 0;
            try
            {
                if (!File.Exists(path)) return false;
                return Int32.TryParse(File.ReadAllText(path).Trim(), out processId) && processId > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void StopRecordedProcess(string path)
        {
            if (TryReadProcessId(path, out int processId))
                StopProcessTree(processId);
        }

        private static bool ProcessIsRunning(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void StopProcessTree(int processId)
        {
            if (processId <= 0) return;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
            }
            catch (ArgumentException)
            {
                // The process has already exited.
            }
            catch (InvalidOperationException)
            {
                // The process has already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process has already exited.
            }
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body, bool skip = false, string? skipReason = null)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.DefinitionOfDoneGate",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag },
                skip: skip,
                skipReason: skipReason);
        }

        #endregion

        private sealed class CompletionFixture
        {
            public MissionService Missions { get; private set; } = null!;

            public AdmiralService Admiral { get; private set; } = null!;

            public Captain Captain { get; private set; } = null!;

            public Mission Mission { get; private set; } = null!;

            public int Landings { get; private set; }

            public static async Task<CompletionFixture> CreateAsync(DatabaseDriver db, string worktree, string buildCommand)
            {
                LoggingModule logging = CreateLogging();
                ArmadaSettings settings = new ArmadaSettings();
                StubGitService git = new StubGitService();
                DockService docks = new DockService(logging, db, settings, git);
                CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                MissionService missions = new MissionService(logging, db, settings, docks, captains, null, git, new DefinitionOfDoneGate(logging));
                AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);
                CompletionFixture fixture = new CompletionFixture { Missions = missions, Admiral = admiral };
                missions.OnMissionComplete = (_, _) => { fixture.Landings++; return Task.CompletedTask; };

                Vessel vessel = new Vessel("dod-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 6), "https://example.invalid/repo.git")
                {
                    DefaultBranch = "main",
                    DefinitionOfDoneEnabled = true,
                    DefinitionOfDoneTimeoutSeconds = 120,
                    DefinitionOfDoneBuildCommand = buildCommand
                };
                vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, BranchName = "topic/dod-gate", Active = true }).ConfigureAwait(false);
                Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("dod-captain") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("Definition-of-Done gate")
                {
                    CaptainId = captain.Id,
                    VesselId = vessel.Id,
                    DockId = dock.Id,
                    Status = MissionStatusEnum.InProgress,
                    StartedUtc = DateTime.UtcNow,
                    BranchName = "topic/dod-gate",
                    DiffSnapshot = "diff --git a/file.txt b/file.txt\n+line\n",
                    AgentOutput = "Changed the file.\n[ARMADA:RESULT] COMPLETE\n",
                    TotalRuntimeMs = 120000
                }).ConfigureAwait(false);
                captain.Assignment = mission;
                await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);

                fixture.Captain = captain;
                fixture.Mission = mission;
                return fixture;
            }
        }
    }
}
