namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Armada.Core.Enums;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;
    using Armada.Server;
    using SyslogLogging;

    /// <summary>
    /// A stub agent runtime for end-to-end dispatch tests: every launch starts a small, long-running OS process the test
    /// owns (<c>sleep</c> on Linux and macOS, <c>ping</c> on Windows) instead of a real agent CLI. Dispatch then behaves
    /// the same on every machine: the launch succeeds whether or not Claude Code or Codex is installed, no model is
    /// called, and the mission stays InProgress until the process is stopped. Register it with
    /// <see cref="InstallOn"/> (the in-process test servers do this for every CLI runtime) and call <see cref="StopAll"/>
    /// when the server or case ends.
    /// </summary>
    public sealed class StubAgentProcesses
    {
        #region Public-Members

        /// <summary>
        /// Runtimes that launch a locally installed agent CLI. ApiEndpoint calls a configured HTTP endpoint and Custom
        /// runtimes are registered by name, so neither depends on what the host has installed.
        /// </summary>
        public static IReadOnlyList<AgentRuntimeEnum> CliRuntimes { get; } = new List<AgentRuntimeEnum>
        {
            AgentRuntimeEnum.ClaudeCode,
            AgentRuntimeEnum.Codex,
            AgentRuntimeEnum.Gemini,
            AgentRuntimeEnum.Cursor,
            AgentRuntimeEnum.Mux,
            AgentRuntimeEnum.OpenCode
        };

        /// <summary>
        /// Process ids of every stub agent started so far.
        /// </summary>
        public IReadOnlyList<int> StartedProcessIds
        {
            get { lock (_Lock) { return new List<int>(_StartedProcessIds); } }
        }

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly object _Lock = new object();
        private readonly List<int> _StartedProcessIds = new List<int>();
        private const int _LifetimeSeconds = 600;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module for the runtimes it creates.</param>
        public StubAgentProcesses(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Make every CLI runtime of a started server launch stub processes from this instance.
        /// </summary>
        /// <param name="server">A started server.</param>
        public void InstallOn(ArmadaServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            foreach (AgentRuntimeEnum runtime in CliRuntimes) server.RuntimeFactory.Override(runtime, CreateRuntime);
        }

        /// <summary>
        /// Create one runtime instance (the factory passed to <see cref="AgentRuntimeFactory.Override"/>).
        /// </summary>
        /// <returns>A runtime whose launches start a long-running stub process.</returns>
        public IAgentRuntime CreateRuntime()
        {
            TestAgentRuntime runtime = new TestAgentRuntime(_Logging);
            if (OperatingSystem.IsWindows())
            {
                runtime.CommandOverride = "ping";
                runtime.ArgsOverride = new List<string> { "-n", (_LifetimeSeconds + 1).ToString(), "127.0.0.1" };
            }
            else
            {
                runtime.CommandOverride = "sleep";
                runtime.ArgsOverride = new List<string> { _LifetimeSeconds.ToString() };
            }

            runtime.OnProcessStarted += (int processId) =>
            {
                lock (_Lock) { _StartedProcessIds.Add(processId); }
            };
            return runtime;
        }

        /// <summary>
        /// True when the stub process with the given id is still running.
        /// </summary>
        /// <param name="processId">Process id.</param>
        /// <returns>True when running.</returns>
        public static bool IsRunning(int processId)
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

        /// <summary>
        /// Kill every stub process this instance started that is still running (by process id).
        /// </summary>
        public void StopAll()
        {
            foreach (int processId in StartedProcessIds)
            {
                try
                {
                    using (Process process = Process.GetProcessById(processId))
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        #endregion
    }
}
