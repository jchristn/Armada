namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// Scripted in-process captain for end-to-end tests. It stands in for the Claude Code runtime (installed through
    /// <see cref="Armada.Runtimes.AgentRuntimeFactory.Override"/>), so no agent CLI is launched: a thread-scoped turn
    /// (Ask Armada, captain chat) runs <see cref="StubCaptainBehavior.OnTurn"/> with the turn's MCP session token; a mission
    /// launch (an MCP port and a log file, without a session token) writes one file in the dock and commits it, then exits 0, so the
    /// server's completion and landing pipeline runs for real; any other prompt (direct captain chat, planning, refinement,
    /// vessel context, categorization) replies with <see cref="StubCaptainBehavior.OnPrompt"/>. Process ids are synthetic.
    /// </summary>
    public sealed class StubCaptainRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "StubCaptain";

        /// <inheritdoc />
        public override bool SupportsResume => false;

        /// <inheritdoc />
        public override bool SupportsPlanningSessions => true;

        #endregion

        #region Private-Members

        private const int MinimumMissionLifetimeMs = 500;
        private static int _PidCounter = 2_100_000_000;
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _Running = new ConcurrentDictionary<int, CancellationTokenSource>();
        private readonly StubCaptainBehavior _Behavior;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="behavior">Script shared by every instance.</param>
        public StubCaptainRuntime(LoggingModule logging, StubCaptainBehavior behavior) : base(logging)
        {
            _Behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        }

        /// <summary>
        /// Replace the server's Claude Code runtime with this stub for the life of the server.
        /// </summary>
        /// <param name="server">In-process server.</param>
        /// <param name="behavior">Script.</param>
        public static void Install(ArmadaServer server, StubCaptainBehavior behavior)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            server.RuntimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () => new StubCaptainRuntime(logging, behavior));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task<int> StartAsync(
            string workingDirectory,
            string prompt,
            Dictionary<string, string>? environment = null,
            string? logFilePath = null,
            string? finalMessageFilePath = null,
            string? model = null,
            Captain? captain = null,
            bool isolateLaunch = false,
            int mcpPort = 0,
            bool showThinking = false,
            CancellationToken token = default)
        {
            int processId = Interlocked.Increment(ref _PidCounter);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Running[processId] = cts;
            string? sessionToken = McpSessionToken;
            RaiseProcessStarted(processId);

            _ = Task.Run(async () =>
            {
                int code;
                try
                {
                    // A mission launch (it has a log file) may also carry a mission-scoped token; only a token launch
                    // without a mission log is an Ask turn.
                    if (!String.IsNullOrEmpty(sessionToken) && mcpPort > 0 && String.IsNullOrEmpty(logFilePath))
                    {
                        _Behavior.TurnPrompts.Enqueue(prompt);
                        StubCaptainTurn turn = new StubCaptainTurn(prompt, workingDirectory, "http://127.0.0.1:" + mcpPort + "/mcp", sessionToken!);
                        string reply = await _Behavior.OnTurn(turn).ConfigureAwait(false);
                        if (!String.IsNullOrEmpty(finalMessageFilePath)) File.WriteAllText(finalMessageFilePath, reply);
                        Func<StubCaptainTurn, string, List<string>>? scripted = _Behavior.TurnOutput;
                        List<string> lines = scripted != null ? scripted(turn, reply) : new List<string>(reply.Split('\n'));
                        foreach (string line in lines) RaiseStdout(processId, line);
                        code = 0;
                    }
                    else if (mcpPort > 0 && !String.IsNullOrEmpty(logFilePath))
                    {
                        _Behavior.MissionDirectories.Enqueue(workingDirectory);
                        Stopwatch lifetime = Stopwatch.StartNew();
                        code = RunMission(processId, workingDirectory, logFilePath);

                        // A real agent process lives at least as long as the server takes to record the launch (the
                        // captain's current mission is written after StartAsync returns); an exit reported before then is
                        // ignored as belonging to no mission. Keep a minimum lifetime like any real process has.
                        int remaining = MinimumMissionLifetimeMs - (int)lifetime.ElapsedMilliseconds;
                        if (remaining > 0) await Task.Delay(remaining).ConfigureAwait(false);

                        Task? exitGate = _Behavior.MissionExitGate;
                        if (exitGate != null) await exitGate.ConfigureAwait(false);
                    }
                    else
                    {
                        _Behavior.PromptTexts.Enqueue(prompt);
                        string reply = _Behavior.OnPrompt(prompt);
                        if (!String.IsNullOrEmpty(finalMessageFilePath)) File.WriteAllText(finalMessageFilePath, reply);
                        foreach (string line in reply.Split('\n')) RaiseStdout(processId, line);
                        WriteLog(logFilePath, new List<string>(reply.Split('\n')));
                        code = 0;
                    }
                }
                catch (Exception ex)
                {
                    _Behavior.Errors.Enqueue(ex.GetType().Name + ": " + ex.Message);
                    code = 1;
                }

                if (_Running.TryRemove(processId, out CancellationTokenSource? running)) running.Dispose();
                RaiseProcessExited(processId, cts.IsCancellationRequested ? -1 : code);
            });

            return Task.FromResult(processId);
        }

        /// <inheritdoc />
        public override Task StopAsync(int processId, CancellationToken token = default)
        {
            if (_Running.TryGetValue(processId, out CancellationTokenSource? cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            return Task.FromResult(_Running.ContainsKey(processId));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override AgentRuntimeEnum RuntimeType => AgentRuntimeEnum.ClaudeCode;

        /// <inheritdoc />
        protected override string GetCommand()
        {
            return "stub-captain";
        }

        /// <inheritdoc />
        protected override List<string> BuildArguments(string workingDirectory, string prompt, string? model, string? finalMessageFilePath, Captain? captain)
        {
            return new List<string>();
        }

        private int RunMission(int processId, string workingDirectory, string? logFilePath)
        {
            List<string> log = new List<string>();
            log.Add("[stub captain] mission started in " + workingDirectory);
            if (!_Behavior.MissionsSucceed)
            {
                log.Add("[stub captain] scripted failure");
                WriteLog(logFilePath, log);
                return 1;
            }

            string fileName = "stub-captain-" + processId + ".txt";
            File.WriteAllText(Path.Combine(workingDirectory, fileName), "Written by the stub captain.\n");
            int add = RunGit(workingDirectory, log, "add", "-A");
            int commit = RunGit(workingDirectory, log, "-c", "user.name=Stub Captain", "-c", "user.email=stub@armada.test", "-c", "commit.gpgsign=false", "commit", "-m", "Stub captain: add " + fileName);
            log.Add("[stub captain] done");
            foreach (string line in log) RaiseStdout(processId, line);
            WriteLog(logFilePath, log);
            return add == 0 && commit == 0 ? 0 : 1;
        }

        private static int RunGit(string workingDirectory, List<string> log, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git");
            info.WorkingDirectory = workingDirectory;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using (Process process = Process.Start(info)!)
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { }
                    log.Add("[stub captain] git " + args[0] + " timed out");
                    return -1;
                }

                log.Add("[stub captain] git " + String.Join(" ", args) + " -> " + process.ExitCode + " " + output.Trim());
                return process.ExitCode;
            }
        }

        private static void WriteLog(string? logFilePath, List<string> lines)
        {
            if (String.IsNullOrEmpty(logFilePath)) return;
            try
            {
                string? dir = Path.GetDirectoryName(logFilePath);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllLines(logFilePath, lines);
            }
            catch (IOException)
            {
            }
        }

        #endregion
    }
}
