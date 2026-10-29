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
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The heartbeat watchdog's stall branch in <see cref="AdmiralService.HealthCheckAsync"/>: a captain whose process
    /// is alive but has produced no output for longer than <see cref="ArmadaSettings.StallThresholdMinutes"/>. The
    /// test process itself stands in for the "alive" agent process, and OnStopAgent is a recorder, so nothing is killed.
    /// </summary>
    public sealed class StallRecoverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.StallRecovery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("stall_under_limit_stops_and_relaunches", "Stalled captain under the recovery limit is stopped and relaunched", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 2;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-(settings.StallThresholdMinutes + 5))).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    Mission? mission = await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertEqual(1, fixture.Stopped.Count, "stalled process is stopped before recovery");
                    AssertEqual(1, fixture.Launched.Count, "agent is relaunched in the same dock");
                    AssertEqual(1, captain!.RecoveryAttempts, "recovery attempt counted");
                    AssertEqual(CaptainStateEnum.Working, captain.State);
                    AssertEqual(MissionStatusEnum.InProgress, mission!.Status, "mission keeps running after recovery");
                    AssertEqual(4242, mission.ProcessId, "mission records the relaunched process");
                }
            }));

            cases.Add(CaseAsync("stall_recovery_exhausted_fails_mission_and_releases", "Stalled captain with recovery exhausted fails the mission and is released", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 1;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-(settings.StallThresholdMinutes + 5))).ConfigureAwait(false);
                    fixture.Captain.RecoveryAttempts = 1;
                    await testDb.Driver.Captains.UpdateAsync(fixture.Captain).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    Mission? mission = await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, mission!.Status);
                    AssertStartsWith("Captain stalled, recovery exhausted.", mission.FailureReason!);
                    AssertContains("configured stall threshold: 10 min", mission.FailureReason!);
                    AssertContains("Remediation:", mission.FailureReason!);
                    AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, mission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertNull(mission.ProcessId, "process id cleared");
                    AssertNotNull(mission.CompletedUtc, "completion time recorded");
                    AssertEqual(1, fixture.Stopped.Count, "stalled process is stopped");
                    AssertEqual(0, fixture.Launched.Count, "no relaunch after exhaustion");
                    AssertEqual(CaptainStateEnum.Idle, captain!.State, "captain released for new work");
                    AssertNull(captain.CurrentMissionId);

                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(fixture.Mission.Id).ConfigureAwait(false);
                    AssertTrue(events.Exists(e => e.EventType == "mission.failed"), "mission.failed event emitted");
                }
            }));

            cases.Add(CaseAsync("fresh_heartbeat_is_left_alone", "Captain with a recent heartbeat is not treated as stalled", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);

                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);

                    Captain? captain = await testDb.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false);
                    AssertEqual(0, fixture.Stopped.Count);
                    AssertEqual(0, fixture.Launched.Count);
                    AssertEqual(0, captain!.RecoveryAttempts);
                    AssertEqual(CaptainStateEnum.Working, captain.State);
                }
            }));

            cases.Add(CaseAsync("tool_wait_is_bounded", "Outstanding tools protect quiet work only until the hard limit", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.ToolWaitHardLimitMinutes = 45;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-20)).ConfigureAwait(false);
                    fixture.Admiral.OnGetToolWaitStartedUtc = _ => DateTime.UtcNow.AddMinutes(-20);
                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);
                    AssertEqual(0, fixture.Stopped.Count, "busy child/tool must survive output silence");
                    fixture.Admiral.OnGetToolWaitStartedUtc = _ => DateTime.UtcNow.AddMinutes(-46);
                    await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);
                    AssertEqual(1, fixture.Stopped.Count, "hung tool must not hold the captain forever");
                }
            }));

            cases.Add(CaseAsync("tool_wait_protocols", "All four runtime protocols match starts and results without extending deadlines", TestTags.Reliability, () =>
            {
                DateTime now = DateTime.UtcNow;
                foreach (var sample in new[]
                {
                    (AgentRuntimeEnum.ClaudeCode, "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"a\",\"name\":\"Bash\"}]}}", "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"a\",\"is_error\":true}]}}"),
                    (AgentRuntimeEnum.Codex, "{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\",\"id\":\"a\"}}", "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"id\":\"a\",\"exit_code\":1}}"),
                    (AgentRuntimeEnum.Mux, "{\"eventType\":\"tool_call_proposed\",\"toolCall\":{\"id\":\"a\",\"name\":\"bash\"}}", "{\"eventType\":\"tool_call_completed\",\"toolCallId\":\"a\",\"success\":false}"),
                    (AgentRuntimeEnum.ApiEndpoint, "[tool-lifecycle] {\"id\":\"a\",\"started\":true}", "[tool-lifecycle] {\"id\":\"a\",\"started\":false}")
                })
                {
                    RuntimeToolWaitTracker tracker = new RuntimeToolWaitTracker();
                    tracker.Observe(sample.Item1, "{}", now);
                    tracker.Observe(sample.Item1, "{bad json}", now);
                    tracker.Observe(sample.Item1, "[tool-lifecycle] []", now);
                    AssertNull(tracker.StartedUtc, "minimal or malformed events must not invent work");
                    tracker.Observe(sample.Item1, sample.Item2, now);
                    tracker.Observe(sample.Item1, sample.Item2, now.AddMinutes(5));
                    AssertEqual<DateTime?>(now, tracker.StartedUtc, "duplicate starts cannot renew a hung call");
                    tracker.Observe(sample.Item1, sample.Item2.Replace("\"a\"", "\"b\""), now.AddMinutes(1));
                    tracker.Observe(sample.Item1, sample.Item3, now.AddMinutes(2));
                    AssertEqual<DateTime?>(now.AddMinutes(1), tracker.StartedUtc, "parallel call remains outstanding");
                    tracker.Observe(sample.Item1, sample.Item3.Replace("\"a\"", "\"b\""), now.AddMinutes(3));
                    AssertNull(tracker.StartedUtc, "error results complete tools too");
                }
                AssertTrue(RuntimeToolWaitTracker.IsStalled(now, now.AddMinutes(-11), null, 10, 60));
                AssertFalse(RuntimeToolWaitTracker.IsStalled(now, now.AddMinutes(-10), null, 10, 60));
                AssertFalse(RuntimeToolWaitTracker.IsStalled(now, now.AddMinutes(-20), now.AddMinutes(-59), 10, 60));
                AssertTrue(RuntimeToolWaitTracker.IsStalled(now, now.AddMinutes(-20), now.AddMinutes(-60), 10, 60), "EOF/missing result cannot extend a call beyond the hard limit");
                ArmadaSettings defaults = System.Text.Json.JsonSerializer.Deserialize<ArmadaSettings>("{}")!;
                AssertEqual(60, defaults.ToolWaitHardLimitMinutes);
                defaults.ToolWaitHardLimitMinutes = 90;
                AssertFalse(RuntimeToolWaitTracker.IsStalled(now, now.AddMinutes(-20), now.AddMinutes(-60), 10, defaults.ToolWaitHardLimitMinutes), "explicit configuration takes effect");
                bool rejected = false;
                try { defaults.ToolWaitHardLimitMinutes = 0; } catch (ArgumentOutOfRangeException) { rejected = true; }
                AssertTrue(rejected, "zero hard limit is rejected");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("stalled_dock_retains_edits", "Exhausted stall recovery preserves tracked and untracked work across orphan sweeps", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 0;
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-20)).ConfigureAwait(false);
                    Dock dock = (await testDb.Driver.Docks.ReadAsync(fixture.Mission.DockId!).ConfigureAwait(false))!;
                    AssertTrue(dock.CreatedUtc < DateTime.UtcNow.AddMinutes(-5), "fixture is old enough to exercise orphan reclamation");
                    Directory.CreateDirectory(dock.WorktreePath!);
                    string file = Path.Combine(dock.WorktreePath!, "uncommitted.txt");
                    await HarborDockGitFixture.GitAsync(dock.WorktreePath!, "init", "-b", dock.BranchName!).ConfigureAwait(false);
                    string tracked = Path.Combine(dock.WorktreePath!, "tracked.txt");
                    File.WriteAllText(tracked, "original");
                    await HarborDockGitFixture.GitAsync(dock.WorktreePath!, "add", "tracked.txt").ConfigureAwait(false);
                    await HarborDockGitFixture.GitAsync(dock.WorktreePath!, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "Seed preservation fixture").ConfigureAwait(false);
                    File.WriteAllText(tracked, "uncommitted tracked edits");
                    File.WriteAllText(file, "valuable edits");
                    try
                    {
                        await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);
                        await fixture.Admiral.HealthCheckAsync().ConfigureAwait(false);
                        Mission mission = (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                        Dock retained = (await testDb.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false))!;
                        AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, mission.FailureKind);
                        AssertEqual(dock.Id, mission.DockId, "mission retains recovery location");
                        AssertTrue(retained.Active, "orphan sweep must not reclaim preserved dock");
                        AssertEqual("valuable edits", File.ReadAllText(file), "untracked work remains byte-for-byte available");
                        AssertEqual("uncommitted tracked edits", File.ReadAllText(tracked));
                        AssertContains("tracked.txt", await HarborDockGitFixture.GitAsync(dock.WorktreePath!, "diff", "--name-only").ConfigureAwait(false));
                    }
                    finally { if (Directory.Exists(dock.WorktreePath!)) Directory.Delete(dock.WorktreePath!, true); }
                }
            }));

            foreach (MissionStatusEnum status in new[] { MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed })
            {
                cases.Add(CaseAsync("unlanded_dock_survives_orphans_" + status, "Orphan sweeps retain an idle captain's unlanded dock in " + status, TestTags.Reliability, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                    ArmadaSettings settings = CreateSettings();
                    StallTestFixture fixture = await StallTestFixture.CreateAsync(testDb.Driver, settings, DateTime.UtcNow.AddMinutes(-20));
                    Mission mission = (await testDb.Driver.Missions.ReadAsync(fixture.Mission.Id))!;
                    mission.Status = status;
                    mission.FailureKind = MissionFailureKindEnum.Infra;
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    Captain captain = (await testDb.Driver.Captains.ReadAsync(mission.CaptainId!))!;
                    captain.State = CaptainStateEnum.Idle;
                    captain.CurrentMissionId = null;
                    captain.CurrentDockId = null;
                    captain.ProcessId = null;
                    await testDb.Driver.Captains.UpdateAsync(captain);
                    Dock dock = (await testDb.Driver.Docks.ReadAsync(mission.DockId!))!;
                    Directory.CreateDirectory(dock.WorktreePath!);
                    string sentinel = Path.Combine(dock.WorktreePath!, "unlanded.txt");
                    await File.WriteAllTextAsync(sentinel, "preserve this work");
                    await fixture.Admiral.HealthCheckAsync();
                    await fixture.Admiral.HealthCheckAsync();
                    AssertTrue(File.Exists(sentinel));
                    AssertTrue((await testDb.Driver.Docks.ReadAsync(dock.Id))!.Active);
                    AssertEqual(status, (await testDb.Driver.Missions.ReadAsync(mission.Id))!.Status);
                }));
            }

            cases.Add(CaseAsync("reused_process_id_is_not_a_live_agent", "A live process that starts one second after the stored launch is not left in progress", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                using (StandInProcess standIn = StartStandIn())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = new ArmadaSettings();
                    string root = TestTemp.NewDirectory("reused-pid");
                    settings.DocksDirectory = root;
                    settings.ReposDirectory = root;
                    settings.MaxMissionRuntimeMinutes = 0;
                    LoggingModule logging = new LoggingModule();
                    logging.Settings.EnableConsole = false;
                    StubGitService git = new StubGitService { IsRepositoryResult = true };
                    IDockService docks = new DockService(logging, db, settings, git);
                    CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                    IMissionService missions = new MissionService(logging, db, settings, docks, captains);
                    IVoyageService voyages = new VoyageService(logging, db);
                    AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, voyages, docks);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("reused-pid-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    ProcessSupervisor.ForgetLaunch(standIn.Process.Id);
                    DateTime startedUtc = standIn.Process.StartTime.ToUniversalTime().AddSeconds(-1);
                    Captain captain = new Captain("reused-pid-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.SetProcess(standIn.Process.Id, startedUtc);
                    captain.LastHeartbeatUtc = DateTime.UtcNow;
                    captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Reused process");
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.StartedUtc = DateTime.UtcNow;
                    mission.SetProcess(standIn.Process.Id, startedUtc);
                    mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    await admiral.HealthCheckAsync().ConfigureAwait(false);

                    bool exited = standIn.Process.WaitForExit(400);
                    AssertFalse(exited, "a process that starts one second after the stored launch is not stopped");
                    AssertFalse(standIn.Process.HasExited, "the stand-in process is still alive");
                    Mission? stored = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, stored!.Status, "the mission is no longer treated as the live process");
                    TestTemp.TryDelete(root);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Stalled Captain Recovery", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            settings.MaxMissionRuntimeMinutes = 0;
            return settings;
        }

        private static StandInProcess StartStandIn()
        {
            string directory = TestTemp.NewDirectory("reused-pid-process");
            ProcessStartInfo start = new ProcessStartInfo();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.WorkingDirectory = directory;
            start.Environment.Clear();
            if (OperatingSystem.IsWindows())
            {
                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                start.FileName = Path.Combine(systemRoot, "System32", "cmd.exe");
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add("ping -n 40 127.0.0.1 >NUL");
                start.Environment["SystemRoot"] = systemRoot;
                start.Environment["PATH"] = Path.Combine(systemRoot, "System32");
            }
            else
            {
                start.FileName = "/bin/sleep";
                start.ArgumentList.Add("40");
            }

            Process process = new Process();
            process.StartInfo = start;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new StandInProcess(process, directory);
        }

        private sealed class StandInProcess : IDisposable
        {
            private readonly string _Directory;

            public StandInProcess(Process process, string directory)
            {
                Process = process ?? throw new ArgumentNullException(nameof(process));
                _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            }

            public Process Process { get; }

            public void Dispose()
            {
                try
                {
                    if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                Process.Dispose();
                TestTemp.TryDelete(_Directory);
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
