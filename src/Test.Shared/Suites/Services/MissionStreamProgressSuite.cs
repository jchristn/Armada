namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End to end: a whole mission whose captain (a shim Claude Code CLI) prints stream-json slowly, one phase per gate the
    /// test opens. While it runs, its activity changes on the Admiral (the mission's current activity) and, for a Harbor
    /// run, in the Harbor's Running now list and job log; when it ends, the mission lands with the captain's final reply
    /// as its AgentOutput, exactly as a text-mode run would. Runs once on an in-process Harbor (real job runner, real
    /// git) and once on the Admiral host.
    /// </summary>
    public sealed class MissionStreamProgressSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MissionStreamProgress";

        private const string FinalReply = "Added the work file and the tests pass.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("harbor_mission_streams_activity_and_lands", "A mission on a Harbor shows changing activity on the Admiral and in Running now while its captain streams, then lands with the captain's final reply", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings discovered = git.Settings.Clone();
                discovered.RepositoryRoots.Add(git.CodeRoot);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_stream", discovered).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "the mission was assigned");

                // Phase 0: the captain starts running the tests.
                AssertTrue(await WaitUntilAsync(() => Task.FromResult(Summary(s.Handler.MissionActivity.Get(mission.Id)) == "Running tests: dotnet test")).ConfigureAwait(false),
                    "the Admiral shows the first activity: " + Summary(s.Handler.MissionActivity.Get(mission.Id)));
                HarborJobInfo? job = null;
                AssertTrue(await WaitUntilAsync(() =>
                {
                    job = harbor.Client.LiveJobs().Find(j => j.MissionId == mission.Id);
                    return Task.FromResult(Summary(job?.Activity) == "Running tests: dotnet test");
                }).ConfigureAwait(false), "Running now shows the first activity");
                AssertEqual("Add a file", job!.Display!.MissionTitle, "Running now shows the mission title");
                AssertEqual("app", job.Display.VesselName);
                AssertEqual(s.CaptainName, job.Display.CaptainName);
                AssertNotNull(job.LogPath, "the job has a log");
                Mission? running = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertTrue(running!.Status == MissionStatusEnum.InProgress || running.Status == MissionStatusEnum.Assigned, "still running: " + running.Status);

                List<HarborLaunchRequest> launches = harbor.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count);
                AssertTrue(launches[0].StructuredProgress, "the Admiral asked for structured progress");
                AssertContains("stream-json", ChatShimCli.ReadRecord(s.Record, "args.txt"), "the Harbor ran the CLI with stream-json");

                // Phase 1: the captain edits a file.
                StreamingShimCli.Gate(s.Record, 1);
                AssertTrue(await WaitUntilAsync(() => Task.FromResult(Summary(s.Handler.MissionActivity.Get(mission.Id)) == "Editing README.md")).ConfigureAwait(false),
                    "the activity changed on the Admiral: " + Summary(s.Handler.MissionActivity.Get(mission.Id)));
                AssertTrue(await WaitUntilAsync(() =>
                {
                    HarborJobInfo? now = harbor.Client.LiveJobs().Find(j => j.MissionId == mission.Id);
                    return Task.FromResult(Summary(now?.Activity) == "Editing README.md");
                }).ConfigureAwait(false), "the activity changed in Running now");
                string logPath = job.LogPath!;

                // Phase 2: the captain commits its work and finishes.
                StreamingShimCli.Gate(s.Record, 2);
                Mission? done = await WaitForEndAsync(s, mission.Id, true).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Complete, done!.Status, done.FailureReason);
                AssertEqual(FinalReply, (done.AgentOutput ?? String.Empty).Trim(), "the final message is the captain's reply, as in text mode");
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", "captain-work.txt").ConfigureAwait(false), "the work landed");
                AssertNull(s.Handler.MissionActivity.Get(mission.Id), "no activity once the captain stopped");

                string harborLog = File.ReadAllText(logPath);
                AssertContains("> Running tests: dotnet test", harborLog, "the Harbor's job log shows the activity");
                AssertContains(FinalReply, harborLog);
                AssertFalse(harborLog.Contains("\"type\":", StringComparison.Ordinal), "no raw stream-json in the Harbor's job log");
                string admiralLog = File.ReadAllText(Path.Combine(s.Settings.LogDirectory, "missions", mission.Id + ".log"));
                AssertContains("> Editing README.md", admiralLog, "the Admiral's mission log mirrors the activity");
                AssertContains(FinalReply, admiralLog);
            }));

            cases.Add(CaseAsync("admiral_host_mission_streams_activity", "A mission on the Admiral host shows changing activity while its captain streams, and keeps the captain's final reply as its output", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin).ConfigureAwait(false);
                await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                AssertTrue(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "the mission was assigned");

                AssertTrue(await WaitUntilAsync(() => Task.FromResult(Summary(s.Handler.MissionActivity.Get(mission.Id)) == "Running tests: dotnet test")).ConfigureAwait(false),
                    "the first activity: " + Summary(s.Handler.MissionActivity.Get(mission.Id)));
                StreamingShimCli.Gate(s.Record, 1);
                AssertTrue(await WaitUntilAsync(() => Task.FromResult(Summary(s.Handler.MissionActivity.Get(mission.Id)) == "Editing README.md")).ConfigureAwait(false),
                    "the activity changed: " + Summary(s.Handler.MissionActivity.Get(mission.Id)));
                Mission? running = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                Captain? captain = await s.Db.Driver.Captains.ReadAsync(running!.CaptainId!).ConfigureAwait(false);
                AssertTrue(await WaitUntilAsync(async () =>
                {
                    captain = await s.Db.Driver.Captains.ReadAsync(running.CaptainId!).ConfigureAwait(false);
                    return captain?.LastHeartbeatUtc != null;
                }).ConfigureAwait(false), "activity counts as output for stall detection (the output heartbeat moved)");

                StreamingShimCli.Gate(s.Record, 2);
                Mission? done = await WaitForEndAsync(s, mission.Id, false).ConfigureAwait(false);
                AssertTrue(done!.Status == MissionStatusEnum.WorkProduced || done.Status == MissionStatusEnum.Complete, "the captain's work was produced: " + done.Status + " " + done.FailureReason);
                AssertEqual(FinalReply, (done.AgentOutput ?? String.Empty).Trim(), "the final message is the captain's reply, as in text mode");
                AssertNull(s.Handler.MissionActivity.Get(mission.Id), "no activity once the captain stopped");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission stream progress (end to end)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string? Summary(RuntimeActivity? activity)
        {
            return activity?.Summary;
        }

        private static async Task<Mission?> WaitForEndAsync(Scenario s, string missionId, bool waitForLanding)
        {
            Mission? done = null;
            bool finished = await WaitUntilAsync(async () =>
            {
                done = await s.Db.Driver.Missions.ReadAsync(missionId).ConfigureAwait(false);
                return done != null && (done.Status == MissionStatusEnum.Complete
                    || done.Status == MissionStatusEnum.Failed
                    || done.Status == MissionStatusEnum.LandingFailed
                    || done.Status == MissionStatusEnum.WorkProduced && done.AgentOutput != null);
            }).ConfigureAwait(false);
            AssertTrue(finished, "the mission finished: " + done?.Status);
            if (waitForLanding && done!.Status == MissionStatusEnum.WorkProduced)
            {
                // Landing follows WorkProduced; wait for its outcome.
                await WaitUntilAsync(async () =>
                {
                    done = await s.Db.Driver.Missions.ReadAsync(missionId).ConfigureAwait(false);
                    return done != null && done.Status != MissionStatusEnum.WorkProduced;
                }).ConfigureAwait(false);
            }

            return done;
        }

        private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(60));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return await condition().ConfigureAwait(false);
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

        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public MissionService Missions { get; }

            public AgentLifecycleHandler Handler { get; }

            public AgentRuntimeFactory Runtimes { get; }

            public string Record { get; }

            public string CaptainName { get; } = "stream-captain-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            private readonly HarborDockGitFixture _Git;
            private readonly string _Root;

            private Scenario(TestDatabase db, HarborDockGitFixture git)
            {
                Db = db;
                _Git = git;
                Logging = new LoggingModule();
                Logging.Settings.EnableConsole = false;
                Settings = new ArmadaSettings();
                Settings.LogDirectory = Path.Combine(git.AdmiralData, "logs");
                Settings.DocksDirectory = Path.Combine(git.AdmiralData, "docks");
                Settings.ReposDirectory = Path.Combine(git.AdmiralData, "repos");

                // The captain: a Claude Code CLI that prints stream-json in three phases, waiting for a gate before each
                // of the last two, and commits its work before the last.
                _Root = TestTemp.NewDirectory("mission_stream_e2e");
                Record = Path.Combine(_Root, "record");
                string init = "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"claude-sonnet-4\"}";
                string bash = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"tu_1\",\"name\":\"Bash\",\"input\":{\"command\":\"dotnet test\",\"description\":\"Running tests\"}}]}}";
                string edit = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"tu_2\",\"name\":\"Edit\",\"input\":{\"file_path\":\"README.md\"}}]}}";
                string text = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Committing the work\"}]}}";
                string result = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"" + FinalReply + "\"}";
                string shim = StreamingShimCli.Write(Path.Combine(_Root, "bin"), "claude", Record,
                    new List<List<string>>
                    {
                        new List<string> { init, bash },
                        new List<string> { edit },
                        new List<string> { text, result }
                    },
                    "captain-work.txt",
                    null);
                Runtimes = new AgentRuntimeFactory(Logging);
                LoggingModule logging = Logging;
                Runtimes.Override(AgentRuntimeEnum.ClaudeCode, () => new ClaudeCodeRuntime(logging) { ExecutablePath = shim });

                Manager = new HarborConnectionManager(new HarborService(db.Driver, Logging), Logging, null);
                GitService gitService = new GitService(Logging);
                DockHostResolver hosts = new DockHostResolver(gitService, Logging, Manager);
                DockService docks = new DockService(Logging, db.Driver, Settings, gitService) { Hosts = hosts };
                CaptainService captains = new CaptainService(Logging, db.Driver, Settings, gitService, docks) { DockHosts = hosts };
                Missions = new MissionService(Logging, db.Driver, Settings, docks, captains, git: gitService) { DockHosts = hosts };
                AdmiralService admiral = new AdmiralService(Logging, db.Driver, Settings, captains, Missions, new VoyageService(Logging, db.Driver), docks);

                Handler = new AgentLifecycleHandler(
                    Logging, db.Driver, Settings, Runtimes, admiral, new MessageTemplateService(Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                Handler.SetHarborConnections(Manager);

                MissionLandingHandler landing = new MissionLandingHandler(Logging, db.Driver, Settings, gitService, new MergeQueueService(Logging, db.Driver, Settings, gitService),
                    new MessageTemplateService(Logging), null, docks, null) { DockHosts = hosts };

                Missions.ResolveDockPlacementAsync = Handler.ResolveDockPlacementAsync;
                Missions.OnGetMissionOutput = Handler.GetAndClearMissionOutput;
                admiral.OnLaunchAgent = Handler.HandleLaunchAgentAsync;
                admiral.OnCaptureDiff = landing.HandleCaptureDiffAsync;
                admiral.OnMissionComplete = landing.HandleMissionCompleteAsync;
            }

            public static async Task<Scenario> CreateAsync(HarborDockGitFixture git)
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db, git);
            }

            /// <summary>
            /// Link an in-process Harbor that runs captains with the real job runner (the shim CLI) and keeps job logs.
            /// </summary>
            public Task<InProcessHarbor> ConnectAsync(string harborId, HarborDockSettings settings)
            {
                HarborDockManager docks = new HarborDockManager(() => settings, Logging);
                LocalHarborJobRunner runner = new LocalHarborJobRunner(Logging, Runtimes, Path.Combine(_Root, "harbor_scratch"))
                {
                    JobLogs = new HarborLogPaths(Path.Combine(_Root, "harbor_logs"))
                };
                return InProcessHarbor.ConnectAsync(Manager, harborId, null, null, Runtimes,
                    new List<string> { "ClaudeCode", "git" }, Logging, null, runner, docks);
            }

            public async Task<Vessel> CreateVesselAsync(string repoUrl)
            {
                Vessel vessel = new Vessel("app", repoUrl);
                vessel.DefaultBranch = "main";
                vessel.LandingMode = LandingModeEnum.LocalMerge;
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync()
            {
                Captain captain = new Captain(CaptainName, AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Idle;
                return await Db.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<Mission> CreateMissionAsync(Vessel vessel)
            {
                Mission mission = new Mission("Add a file", "Add a file to the repository");
                mission.VesselId = vessel.Id;
                mission.Status = MissionStatusEnum.Pending;
                return await Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            public void Dispose()
            {
                // Let a shim still waiting at a gate (a failed assertion) finish instead of leaking.
                for (int gate = 1; gate <= 2; gate++)
                {
                    try { StreamingShimCli.Gate(Record, gate); } catch { }
                }

                Db.Dispose();
            }
        }

        #endregion
    }
}
