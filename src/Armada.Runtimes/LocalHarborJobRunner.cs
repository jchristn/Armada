namespace Armada.Runtimes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Launches captains on the Harbor host in-process, using the local agent runtimes, and streams their
    /// lifecycle back to the link client. This is the Harbor-side counterpart to the Admiral's
    /// <see cref="RemoteAgentRuntime"/>: the Admiral sends a launch request, this runs the captain here, and
    /// reports started/output/exited over the link.
    /// </summary>
    public class LocalHarborJobRunner : IHarborJobRunner
    {
        #region Private-Members

        private readonly IHostProcessExecutor _Executor;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, JobEntry> _Jobs = new ConcurrentDictionary<string, JobEntry>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public LocalHarborJobRunner(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Executor = new LocalHostProcessExecutor(new AgentRuntimeFactory(_Logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task StartAsync(
            HarborLaunchRequest request,
            string? mcpBaseUrl,
            Action<int> onStarted,
            Action<HarborOutputStreamEnum, string> onOutput,
            Action<int> onExited,
            CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            AgentRuntimeEnum runtimeType;
            if (!Enum.TryParse(request.Runtime, true, out runtimeType))
                throw new NotSupportedException("Unknown runtime requested: " + request.Runtime);

            IAgentRuntime runtime = _Executor.CreateRuntime(runtimeType);
            string jobId = request.JobId;

            runtime.OnProcessStarted += processId =>
            {
                _Jobs[jobId] = new JobEntry(runtime, processId);
                onStarted(processId);
            };

            // The lifecycle handler consumes OnOutputReceived (both stdout and stderr) for mission output, so
            // forward every line as Stdout. A finer stdout/stderr split can follow when chat/planning is
            // delegated.
            runtime.OnOutputReceived += (processId, line) => onOutput(HarborOutputStreamEnum.Stdout, line);

            runtime.OnProcessExited += (processId, exitCode) =>
            {
                _Jobs.TryRemove(jobId, out JobEntry? _);
                onExited(exitCode ?? -1);
            };

            _Logging.Info("[LocalHarborJobRunner] launching " + runtimeType + " for job " + jobId + " in " + request.WorkingDirectory);

            await runtime.StartAsync(
                request.WorkingDirectory,
                request.Prompt ?? string.Empty,
                request.Environment,
                model: request.Model,
                token: token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            if (_Jobs.TryRemove(jobId, out JobEntry? entry))
                await entry!.Runtime.StopAsync(entry.ProcessId, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Members-Types

        private sealed class JobEntry
        {
            public IAgentRuntime Runtime { get; }

            public int ProcessId { get; }

            public JobEntry(IAgentRuntime runtime, int processId)
            {
                Runtime = runtime;
                ProcessId = processId;
            }
        }

        #endregion
    }
}
