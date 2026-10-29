namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Recovery of a crashed pi mission end to end: a real dock (git repository) gets the generated AGENTS.override.md
    /// from MissionService and the generated .pi/mcp.json from a real PiRuntime launch of the recording stub; the pi
    /// process dies and the Admiral never handles the exit; the Admiral health check then finds the orphan, removes both
    /// generated files, leaves the dock's git status clean with the captain's commit intact, holds the mission as
    /// WorkProduced, and the next instructions generation in that dock carries the repository's AGENTS.md again.
    /// </summary>
    public sealed class PiCrashRecoverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PiCrashRecovery";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: "crashed_pi_mission_is_cleaned_and_recoverable",
                displayName: "A crashed pi mission's leftover .pi/mcp.json and AGENTS.override.md are removed by the health check and the mission is held as WorkProduced with its work",
                executeAsync: (CancellationToken ct) => RunAsync(),
                tags: new List<string> { TestTags.Reliability },
                skipReason: OperatingSystem.IsWindows() ? "The recording pi stub is a POSIX shell script." : null));
            return new TestSuiteDescriptor(SuiteId, "pi crash recovery", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RunAsync()
        {
            using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            DatabaseDriver db = testDb.Driver;
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            StubGitService git = new StubGitService();
            DockService docks = new DockService(logging, db, settings, git);
            CaptainService captains = new CaptainService(logging, db, settings, git, docks);
            MissionService missions = new MissionService(logging, db, settings, docks, captains);
            AdmiralService admiral = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), docks);

            string root = TestTemp.NewDirectory("pi_crash");
            try
            {
                string worktree = Path.Combine(root, "dock");
                Directory.CreateDirectory(worktree);
                Git(worktree, "init", "-q");
                Git(worktree, "config", "user.email", "test@example.invalid");
                Git(worktree, "config", "user.name", "Test");
                File.WriteAllText(Path.Combine(worktree, "AGENTS.md"), "Repository rule: keep pi crash tests honest.\n");
                Git(worktree, "add", "AGENTS.md");
                Git(worktree, "commit", "-q", "-m", "initial");

                Vessel vessel = await db.Vessels.CreateAsync(new Vessel("pi-crash", "https://example.invalid/pi-crash.git")).ConfigureAwait(false);
                Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain("pi-crash", AgentRuntimeEnum.Pi) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("pi crash") { CaptainId = captain.Id, VesselId = vessel.Id, DockId = dock.Id, Status = MissionStatusEnum.InProgress, StartedUtc = DateTime.UtcNow }).ConfigureAwait(false);

                // The real mission setup: AGENTS.override.md carrying the repository's AGENTS.md, git-excluded.
                await missions.GenerateClaudeMdAsync(worktree, mission, vessel, captain).ConfigureAwait(false);
                string overridePath = Path.Combine(worktree, "AGENTS.override.md");
                AssertTrue(File.Exists(overridePath), "AGENTS.override.md was generated");
                AssertContains("Repository rule", File.ReadAllText(overridePath), "the repository's AGENTS.md is carried in");

                // The real launch: a mission-scoped token makes PiRuntime write the git-excluded .pi/mcp.json.
                string stubDir = Path.Combine(root, "stub");
                string stub = StubAgentProcesses.WritePiStub(stubDir);
                File.WriteAllText(Path.Combine(stubDir, "pi-sleep.txt"), "60");
                PiRuntime runtime = new PiRuntime(logging) { ExecutablePath = stub, McpSessionToken = "crash-scoped-token", McpAllowWorkingDirectoryFiles = false };
                TaskCompletionSource<int?> exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                runtime.OnProcessExited += (int pid, int? code) => exited.TrySetResult(code);
                int processId = await runtime.StartAsync(worktree, "do the work", null, captain: captain, isolateLaunch: true, mcpPort: 7890).ConfigureAwait(false);
                AssertTrue(await WaitForAsync(() => File.Exists(Path.Combine(stubDir, "pi-mcp.json"))).ConfigureAwait(false), "the stub pi started and saw .pi/mcp.json");
                string mcpPath = Path.Combine(worktree, PiProjectMcpConfig.RelativePath);
                string leftover = File.ReadAllText(mcpPath);
                AssertTrue(PiProjectMcpConfig.IsGenerated(leftover), "the launch wrote the generated .pi/mcp.json");

                // The captain produced work while running; the generated files never show up in git.
                File.WriteAllText(Path.Combine(worktree, "work.txt"), "done\n");
                AssertEqual("?? work.txt", Git(worktree, "status", "--porcelain").Trim(), "only the captain's work is visible to git");
                Git(worktree, "add", "-A");
                Git(worktree, "commit", "-q", "-m", "captain work");

                // pi dies; the Admiral crashed too, so its exit handler never ran. The runtime of this test process still
                // cleans up after the kill, so put back the file the launch left behind.
                mission.ProcessId = processId;
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                using (Process process = Process.GetProcessById(processId)) process.Kill(true);
                AssertTrue(await Task.WhenAny(exited.Task, Task.Delay(30000)).ConfigureAwait(false) == exited.Task, "the stub pi exited");
                Directory.CreateDirectory(Path.GetDirectoryName(mcpPath)!);
                File.WriteAllText(mcpPath, leftover);

                // After the restart the captain is no longer on the mission, so the health check sees an orphan.
                Captain restarted = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                restarted.State = CaptainStateEnum.Idle;
                restarted.CurrentMissionId = null;
                restarted.CurrentDockId = null;
                restarted.ProcessId = null;
                await db.Captains.UpdateAsync(restarted).ConfigureAwait(false);
                await admiral.HealthCheckAsync().ConfigureAwait(false);

                AssertFalse(File.Exists(mcpPath), "the leftover .pi/mcp.json was removed");
                AssertFalse(Directory.Exists(Path.Combine(worktree, ".pi")), "the empty .pi directory was removed");
                AssertFalse(File.Exists(overridePath), "the leftover generated AGENTS.override.md was removed");
                AssertTrue(File.Exists(Path.Combine(worktree, "AGENTS.md")), "the repository's AGENTS.md is untouched");
                AssertEqual("", Git(worktree, "status", "--porcelain").Trim(), "the dock is clean");
                AssertContains("captain work", Git(worktree, "log", "--oneline"), "the captain's commit is intact");

                Mission after = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.WorkProduced, after.Status, "held for operator recovery, not completed");
                AssertNull(after.ProcessId, "the dead process was released");
                AssertContains("not verified", after.FailureReason ?? "");
                AssertEqual(MissionFailureKindEnum.Infra, after.FailureKind, "the orphan hold records a kind");
                List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                AssertTrue(events.Any(e => e.EventType == "mission.orphan_recovered"), "the recovery is recorded");

                // The dock is reusable: the next generation carries the repository's AGENTS.md again (a leftover file would
                // have been read back as the dock's own instructions) and holds one set of mission instructions.
                await missions.GenerateClaudeMdAsync(worktree, after, vessel, captain).ConfigureAwait(false);
                string regenerated = File.ReadAllText(overridePath);
                AssertContains("Repository rule", regenerated, "the repository's AGENTS.md is carried again");
                AssertEqual(1, CountOf(regenerated, "# Mission Instructions"), "one set of mission instructions");
                AssertEqual("", Git(worktree, "status", "--porcelain").Trim(), "the regenerated file is git-excluded");
            }
            finally
            {
                TestTemp.TryDelete(root);
            }
        }

        private static int CountOf(string text, string value)
        {
            int count = 0;
            int at = text.IndexOf(value, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return condition();
        }

        private static string Git(string workingDirectory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            AssertEqual(0, process.ExitCode, "git " + String.Join(" ", args) + ": " + errors);
            return output;
        }

        #endregion
    }
}
