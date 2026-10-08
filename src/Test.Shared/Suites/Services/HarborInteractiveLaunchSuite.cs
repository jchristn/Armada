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
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Ask;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using Armada.Server.Ask;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Interactive captain launches (Ask Armada turns, direct captain chat, planning sessions) route through the same
    /// Harbor selection as missions. With an eligible Harbor connected the captain's CLI runs on the Harbor (a real
    /// in-process Harbor link and job runner driving shim CLIs), in a Harbor scratch directory, with the turn's MCP token,
    /// and its reply streams back; the Admiral-host runtime is never started. With requireHarborForLaunch on and no
    /// Harbor, or with the CLI missing on the Admiral host or on the Harbor, the turn fails with a typed error instead
    /// of the raw process-start exception. Stopping a turn kills the process on the Harbor.
    /// </summary>
    public sealed class HarborInteractiveLaunchSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborInteractiveLaunch";
        private const string McpUrl = "http://127.0.0.1:59999/mcp";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("ask_turn_runs_on_connected_harbor", "An Ask turn with an eligible Harbor connected runs the CLI on the Harbor and streams its reply back", TestTags.Positive, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectHarborAsync("hbr_ask_ok", Constants.DefaultTenantId, "usr_ask").ConfigureAwait(false);
                AuthContext owner = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_ask", false, true, "Test");
                Captain captain = await s.CreateCaptainAsync("ask-harbor", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                AskThread thread = await s.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);

                AskTurnStart start = await s.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Which voyages are running?" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);
                await s.WaitForTurnEndAsync(thread.Id).ConfigureAwait(false);

                AskMessagePage page = (await s.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AskMessage reply = page.Messages.Last();
                AssertEqual(AskMessageRoleEnum.Assistant, reply.Role, "the turn produced an assistant reply: " + reply.ContentText);
                AssertEqual("Hello from the Harbor", reply.ContentText);
                AssertTrue(s.EventsFor("usr_ask", "ask.chunk").Count >= 2, "the reply streamed in chunks over the link");

                AssertEqual(0, s.LocalLaunches, "the Admiral-host runtime was never created");
                AssertEqual(String.Empty, ChatShimCli.ReadRecord(s.LocalRecord, "cwd.txt"), "nothing ran on the Admiral host");

                List<HarborLaunchRequest> launches = harbor.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count, "one launch on the Harbor");
                AssertTrue(launches[0].ScratchWorkingDirectory, "the launch may use a Harbor scratch directory");
                AssertEqual(String.Empty, launches[0].WorkingDirectory, "no Admiral-host path is sent");
                AssertTrue(launches[0].StreamJsonOutput, "Claude Code streams JSON on the Harbor");
                AssertNotNull(launches[0].McpSessionToken, "the thread-scoped MCP token travels with the launch");

                string cwd = ChatShimCli.ReadRecord(s.HarborRecord, "cwd.txt");
                AssertContains(Path.GetFileName(harbor.ScratchRoot), cwd, "the CLI ran in the Harbor's scratch area");
                AssertFalse(cwd.Contains("armada-chat-", StringComparison.Ordinal), "not in the Admiral's chat directory");
                AssertContains(launches[0].McpSessionToken!, ChatShimCli.ReadRecord(s.HarborRecord, "token.txt"), "the CLI received the turn's MCP token");
                AssertContains("--strict-mcp-config", ChatShimCli.ReadRecord(s.HarborRecord, "args.txt"), "the MCP connection is bound to the advertised URL");
                AssertContains("Which voyages are running?", ChatShimCli.ReadRecord(s.HarborRecord, "prompt.txt"), "the prompt reached the CLI on stdin");

                bool cleaned = await WaitUntilAsync(() => !Directory.Exists(Path.Combine(harbor.ScratchRoot, "scratch")) || Directory.GetDirectories(Path.Combine(harbor.ScratchRoot, "scratch")).Length == 0).ConfigureAwait(false);
                AssertTrue(cleaned, "the Harbor removed the scratch directory when the job ended");
            }));

            cases.Add(CaseAsync("ask_turn_without_harbor_runs_locally", "With requireHarborForLaunch off and no Harbor connected, an Ask turn still runs on the Admiral host", TestTags.Positive, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                AuthContext owner = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_local", false, true, "Test");
                Captain captain = await s.CreateCaptainAsync("ask-local", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                AskThread thread = await s.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);

                await s.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Status?" }).ConfigureAwait(false);
                await s.WaitForTurnEndAsync(thread.Id).ConfigureAwait(false);

                AskMessagePage page = (await s.Threads.EnumerateMessagesAsync(owner, thread.Id, null).ConfigureAwait(false))!;
                AssertEqual("Hello from the Admiral", page.Messages.Last().ContentText);
                AssertEqual(1, s.LocalLaunches, "ran on the Admiral host");
            }));

            cases.Add(CaseAsync("require_harbor_without_harbor_fails_typed", "With requireHarborForLaunch on and no eligible Harbor, a chat turn fails with HarborRequired instead of starting a local process", TestTags.Negative, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                s.Settings.RequireHarborForLaunch = true;
                Captain captain = await s.CreateCaptainAsync("policy-none", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);

                CaptainChatTurnResult none = await s.Chat.RunTurnAsync(s.TurnOptions(captain, "usr_policy")).ConfigureAwait(false);
                AssertFalse(none.Response.Success, "the turn failed");
                AssertEqual(CaptainChatErrorCodeEnum.HarborRequired, none.Response.ErrorCode);
                AssertContains("No Harbor is connected to run this captain", none.Response.Error ?? String.Empty);

                // A Harbor owned by someone else does not count either.
                using InProcessHarbor other = await s.ConnectHarborAsync("hbr_policy_other", Constants.DefaultTenantId, "usr_someone_else").ConfigureAwait(false);
                CaptainChatTurnResult foreign = await s.Chat.RunTurnAsync(s.TurnOptions(captain, "usr_policy")).ConfigureAwait(false);
                AssertEqual(CaptainChatErrorCodeEnum.HarborRequired, foreign.Response.ErrorCode, "another user's Harbor is not eligible");
                AssertEqual(0, other.Runner.LaunchSnapshot().Count, "nothing was sent to another user's Harbor");
                AssertEqual(0, s.LocalLaunches, "nothing started on the Admiral host");
            }));

            cases.Add(CaseAsync("local_missing_cli_fails_typed", "A chat turn on the Admiral host whose CLI is missing fails with RuntimeNotInstalled and suggests a Harbor", TestTags.Negative, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                s.LocalExecutable = Path.Combine(s.Root, "not-installed", "claude-missing");
                Captain captain = await s.CreateCaptainAsync("missing-cli", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);

                CaptainChatTurnResult result = await s.Chat.RunTurnAsync(s.TurnOptions(captain, "usr_missing")).ConfigureAwait(false);
                AssertFalse(result.Response.Success, "the turn failed");
                AssertEqual(CaptainChatErrorCodeEnum.RuntimeNotInstalled, result.Response.ErrorCode);
                string error = result.Response.Error ?? String.Empty;
                AssertContains("not installed on the Admiral host", error);
                AssertContains("Harbor", error, "suggests connecting a Harbor");
                AssertFalse(error.Contains("An error occurred trying to start process", StringComparison.Ordinal), "not the raw .NET exception text");
            }));

            cases.Add(CaseAsync("harbor_missing_cli_fails_fast_with_harbor_reason", "A Harbor whose CLI is missing fails the turn at once with the Harbor's reason", TestTags.Negative, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                s.HarborExecutable = Path.Combine(s.Root, "not-installed", "claude-missing");
                using InProcessHarbor harbor = await s.ConnectHarborAsync("hbr_missing_cli", Constants.DefaultTenantId, "usr_hmissing").ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("harbor-missing-cli", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);

                Stopwatch elapsed = Stopwatch.StartNew();
                CaptainChatTurnResult result = await s.Chat.RunTurnAsync(s.TurnOptions(captain, "usr_hmissing")).ConfigureAwait(false);
                elapsed.Stop();

                AssertFalse(result.Response.Success, "the turn failed");
                AssertEqual(CaptainChatErrorCodeEnum.HarborLaunchFailed, result.Response.ErrorCode);
                AssertContains("hbr_missing_cli", result.Response.Error ?? String.Empty, "names the Harbor");
                AssertContains("not installed", result.Response.Error ?? String.Empty, "carries the Harbor's reason");
                AssertTrue(elapsed.Elapsed < TimeSpan.FromSeconds(30), "failed without waiting out the 60 s start timeout");
                AssertEqual(0, s.LocalLaunches, "did not fall back to the Admiral host");
            }));

            cases.Add(CaseAsync("stop_kills_the_harbor_process", "Stopping an Ask turn that runs on a Harbor kills the captain process on the Harbor", TestTags.Positive, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                s.HarborSleeps = true;
                using InProcessHarbor harbor = await s.ConnectHarborAsync("hbr_stop", Constants.DefaultTenantId, "usr_stop").ConfigureAwait(false);
                AuthContext owner = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_stop", false, true, "Test");
                Captain captain = await s.CreateCaptainAsync("stop-harbor", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                AskThread thread = await s.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);

                await s.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Take your time." }).ConfigureAwait(false);
                AssertTrue(await WaitUntilAsync(() => harbor.Runner.StartedSnapshot().Count == 1).ConfigureAwait(false), "the captain started on the Harbor");
                int pid = harbor.Runner.StartedSnapshot()[0];
                AssertTrue(IsRunning(pid), "the Harbor process is running");

                int status = await s.Turns.CancelAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(200, status, "the running turn was cancelled");
                await s.WaitForTurnEndAsync(thread.Id).ConfigureAwait(false);

                AssertTrue(await WaitUntilAsync(() => harbor.Runner.StopSnapshot().Count == 1).ConfigureAwait(false), "the Admiral asked the Harbor to stop the job");
                AssertTrue(await WaitUntilAsync(() => !IsRunning(pid)).ConfigureAwait(false), "the captain process on the Harbor is gone");
                List<AskTurnEventPayload> turns = s.EventsFor("usr_stop", "ask.turn").Select(e => AskTurnEventPayload.From(e.Payload)).ToList();
                AssertEqual("cancelled", turns.Last().State, "the turn ended as cancelled");
                AssertEqual(0, s.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("direct_chat_runs_on_harbor_with_final_message", "Direct captain chat runs on the Harbor, and the CLI's final-message file is returned over the link", TestTags.Positive, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectHarborAsync("hbr_codex", Constants.DefaultTenantId, "usr_codex").ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("codex-harbor", AgentRuntimeEnum.Codex).ConfigureAwait(false);
                AuthContext admin = AuthContext.Authenticated(Constants.DefaultTenantId, "usr_codex", true, true, "Test");

                CaptainChatResponse response = await s.Chat.ChatAsync(captain.Id, new CaptainChatRequest { Message = "Summarize the fleet." }, admin).ConfigureAwait(false);

                AssertTrue(response.Success, "the chat succeeded: " + response.Error);
                AssertEqual("FinalFromHarborCodex", response.Reply, "the reply is the final message, not the CLI's progress output");
                AssertEqual(0, s.LocalLaunches, "the Admiral-host runtime was never created");
                List<HarborLaunchRequest> launches = harbor.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count, "one launch on the Harbor");
                AssertTrue(launches[0].ReturnFinalMessage, "the Harbor was asked to return the final message");
                AssertContains("--output-last-message", ChatShimCli.ReadRecord(s.HarborRecord, "args.txt"));
                AssertContains(Path.GetFileName(harbor.ScratchRoot), ChatShimCli.ReadRecord(s.HarborRecord, "cwd.txt"), "ran in the Harbor's scratch area");
            }));

            cases.Add(CaseAsync("planning_turn_runs_on_harbor", "A planning-session turn runs on the Harbor with its context inlined, and its reply streams back", TestTags.Positive, async () =>
            {
                using Scenario s = await Scenario.CreateAsync().ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectHarborAsync("hbr_plan", null, null).ConfigureAwait(false);
                PlanningSessionCoordinator planning = s.CreatePlanningCoordinator();
                Vessel vessel = await s.CreateVesselAsync("plan-vessel").ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync("planner-harbor", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);

                PlanningSession session = await planning.CreateAsync(null, null, captain, vessel, new PlanningSessionCreateRequest { Title = "Plan on the Harbor" }).ConfigureAwait(false);
                await planning.SendMessageAsync(session, "Plan the API hardening.").ConfigureAwait(false);

                string lastReply = String.Empty;
                bool answered = await WaitUntilAsync(async () =>
                {
                    PlanningSession? current = await s.Db.Driver.PlanningSessions.ReadAsync(session.Id).ConfigureAwait(false);
                    List<PlanningSessionMessage> messages = await s.Db.Driver.PlanningSessionMessages.EnumerateBySessionAsync(session.Id).ConfigureAwait(false);
                    PlanningSessionMessage? reply = messages.Where(m => m.Role == "Assistant").OrderBy(m => m.Sequence).LastOrDefault();
                    lastReply = reply?.Content ?? String.Empty;
                    return current != null && current.Status == PlanningSessionStatusEnum.Active && lastReply == "Hello from the Harbor";
                }).ConfigureAwait(false);
                AssertTrue(answered, "the planning reply came back from the Harbor (last reply '" + lastReply + "')");

                AssertEqual(0, s.LocalLaunches, "the Admiral-host runtime was never created");
                List<HarborLaunchRequest> launches = harbor.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count, "one launch on the Harbor");
                AssertTrue(launches[0].ScratchWorkingDirectory, "a missing dock path falls back to the Harbor's scratch area");
                AssertTrue(launches[0].StreamJsonOutput, "the planning turn streams JSON");
                string prompt = ChatShimCli.ReadRecord(s.HarborRecord, "prompt.txt");
                AssertContains("## Session context", prompt, "the Admiral-side context file was inlined");
                AssertContains("Plan the API hardening.", prompt, "the transcript reached the Harbor");
                await planning.StopAsync(session).ConfigureAwait(false);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor Interactive Launches",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static bool IsRunning(int processId)
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

        private static Task<bool> WaitUntilAsync(Func<bool> condition)
        {
            return WaitUntilAsync(() => Task.FromResult(condition()));
        }

        private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(25).ConfigureAwait(false);
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

        #region Nested-Types

        /// <summary>
        /// An Admiral with a test database, a Harbor connection manager, the launch router, a captain chat service, and an
        /// Ask turn coordinator. Admiral-host runtimes and Harbor runtimes point at different shim CLIs (and record into
        /// different directories), and every Admiral-host runtime creation is counted.
        /// </summary>
        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public CaptainLaunchRouter Router { get; }

            public CaptainChatService Chat { get; }

            public AskThreadService Threads { get; }

            public AskTurnCoordinator Turns { get; }

            public string Root { get; }

            public string LocalRecord { get; }

            public string HarborRecord { get; }

            public string? LocalExecutable { get; set; } = null;

            public string? HarborExecutable { get; set; } = null;

            public bool HarborSleeps { get; set; } = false;

            public int LocalLaunches => _LocalLaunches;

            private readonly AgentRuntimeFactory _LocalRuntimes;
            private readonly List<AskRecordedEvent> _Events = new List<AskRecordedEvent>();
            private int _LocalLaunches = 0;

            private Scenario(TestDatabase db)
            {
                Db = db;
                Logging = new LoggingModule();
                Logging.Settings.EnableConsole = false;
                Root = TestTemp.NewDirectory("harbor_interactive");
                LocalRecord = Path.Combine(Root, "local-record");
                HarborRecord = Path.Combine(Root, "harbor-record");

                Settings = new ArmadaSettings
                {
                    DataDirectory = Root,
                    DatabasePath = Path.Combine(Root, "armada.db"),
                    LogDirectory = Path.Combine(Root, "logs"),
                    DocksDirectory = Path.Combine(Root, "docks"),
                    ReposDirectory = Path.Combine(Root, "repos")
                };
                Settings.InitializeDirectories();

                string localShims = Path.Combine(Root, "local-bin");
                string localClaude = ChatShimCli.WriteClaudeStreamShim(localShims, LocalRecord, new List<string> { "Hello from ", "the Admiral" });
                _LocalRuntimes = new AgentRuntimeFactory(Logging);
                _LocalRuntimes.Override(AgentRuntimeEnum.ClaudeCode, () =>
                {
                    Interlocked.Increment(ref _LocalLaunches);
                    return new ClaudeCodeRuntime(Logging) { ExecutablePath = LocalExecutable ?? localClaude };
                });
                _LocalRuntimes.Override(AgentRuntimeEnum.Codex, () =>
                {
                    Interlocked.Increment(ref _LocalLaunches);
                    return new CodexRuntime(Logging) { ExecutablePath = LocalExecutable ?? localClaude };
                });

                Manager = new HarborConnectionManager(new HarborService(db.Driver, Logging), Logging, McpUrl);
                Router = new CaptainLaunchRouter(Settings, _LocalRuntimes, Manager, null, Logging);
                Chat = new CaptainChatService(db.Driver, _LocalRuntimes, null, null, null, 0, Logging);
                Chat.LaunchRouter = Router;

                Threads = new AskThreadService(db.Driver, Settings, Logging);
                Turns = new AskTurnCoordinator(db.Driver, Threads, Chat, new SessionTokenService(), null, Settings, Logging);
                Threads.ActiveTurnResolver = Turns.ActiveTurnId;
                Threads.OnUserEvent = (tenantId, userId, eventType, payload) =>
                {
                    lock (_Events) _Events.Add(new AskRecordedEvent { TenantId = tenantId, UserId = userId, EventType = eventType, Payload = payload });
                };
            }

            public static async Task<Scenario> CreateAsync()
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db);
            }

            public async Task<InProcessHarbor> ConnectHarborAsync(string harborId, string? tenantId, string? userId)
            {
                string harborShims = Path.Combine(Root, "harbor-bin-" + harborId);
                string claude = HarborSleeps
                    ? ChatShimCli.WriteSleepingShim(harborShims, "claude", HarborRecord)
                    : ChatShimCli.WriteClaudeStreamShim(harborShims, HarborRecord, new List<string> { "Hello from ", "the Harbor" });
                string codex = ChatShimCli.WriteCodexFinalMessageShim(harborShims, HarborRecord, "FinalFromHarborCodex");

                AgentRuntimeFactory harborRuntimes = new AgentRuntimeFactory(Logging);
                string? missing = HarborExecutable;
                harborRuntimes.Override(AgentRuntimeEnum.ClaudeCode, () => new ClaudeCodeRuntime(Logging) { ExecutablePath = missing ?? claude });
                harborRuntimes.Override(AgentRuntimeEnum.Codex, () => new CodexRuntime(Logging) { ExecutablePath = missing ?? codex });

                return await InProcessHarbor.ConnectAsync(
                    Manager,
                    harborId,
                    tenantId,
                    userId,
                    harborRuntimes,
                    new List<string> { "git", "ClaudeCode", "Codex" },
                    Logging).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync(string name, AgentRuntimeEnum runtime)
            {
                return await Db.Driver.Captains.CreateAsync(new Captain(name, runtime) { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
            }

            public async Task<Vessel> CreateVesselAsync(string name)
            {
                Fleet fleet = await Db.Driver.Fleets.CreateAsync(new Fleet("fleet-" + name)).ConfigureAwait(false);
                Vessel vessel = new Vessel(name, "https://github.com/test/" + name + ".git")
                {
                    FleetId = fleet.Id,
                    LocalPath = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    WorkingDirectory = Path.Combine(Settings.ReposDirectory, name + ".git"),
                    DefaultBranch = "main"
                };
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public PlanningSessionCoordinator CreatePlanningCoordinator()
            {
                StubGitService git = new StubGitService();
                DockService docks = new DockService(Logging, Db.Driver, Settings, git);
                ICaptainService captains = new CaptainService(Logging, Db.Driver, Settings, git, docks);
                IMissionService missions = new MissionService(Logging, Db.Driver, Settings, docks, captains);
                AdmiralService admiral = new AdmiralService(Logging, Db.Driver, Settings, captains, missions, new VoyageService(Logging, Db.Driver), docks);
                PlanningSessionCoordinator coordinator = new PlanningSessionCoordinator(
                    Logging,
                    Db.Driver,
                    Settings,
                    docks,
                    admiral,
                    _LocalRuntimes,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                coordinator.LaunchRouter = Router;
                return coordinator;
            }

            public CaptainChatTurnOptions TurnOptions(Captain captain, string userId)
            {
                CaptainChatTurnOptions options = new CaptainChatTurnOptions();
                options.Captain = captain;
                options.Prompt = "User: hello";
                options.TenantId = Constants.DefaultTenantId;
                options.UserId = userId;
                options.TimeoutMs = 60000;
                return options;
            }

            public List<AskRecordedEvent> EventsFor(string userId, string eventType)
            {
                lock (_Events) return _Events.Where(e => e.UserId == userId && e.EventType == eventType).ToList();
            }

            public async Task WaitForTurnEndAsync(string threadId)
            {
                bool done = await WaitUntilAsync(() => Turns.ActiveTurnId(threadId) == null).ConfigureAwait(false);
                AssertTrue(done, "turn finished");
                bool reported = await WaitUntilAsync(() =>
                {
                    lock (_Events)
                    {
                        return _Events.Any(e => e.EventType == "ask.turn" && AskTurnEventPayload.From(e.Payload).State != "started");
                    }
                }).ConfigureAwait(false);
                AssertTrue(reported, "turn end reported");
            }

            public void Dispose()
            {
                Db.Dispose();
                TestTemp.TryDelete(Root);
            }
        }

        #endregion
    }
}
