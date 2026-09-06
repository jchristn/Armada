namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// An agent runtime whose captain process runs on a Harbor host, not the Admiral. It sends a launch
    /// request over the Harbor link and translates the Harbor's reported lifecycle (started, output, exited)
    /// into the ordinary <see cref="IAgentRuntime"/> events, so the agent lifecycle handler works against it
    /// exactly as it does a local process. Liveness and termination route to the Harbor by the reported
    /// process id.
    /// </summary>
    public class RemoteAgentRuntime : IAgentRuntime, IHarborJobListener
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => _RuntimeType.ToString();

        /// <inheritdoc />
        public bool SupportsResume => false;

        /// <inheritdoc />
        public bool SupportsPlanningSessions => false;

        /// <inheritdoc />
        public event Action<int, string>? OnOutputReceived;

        /// <inheritdoc />
        public event Action<int, string>? OnStdoutReceived;

        /// <inheritdoc />
        public event Action<int>? OnProcessStarted;

        /// <inheritdoc />
        public event Action<int, int?>? OnProcessExited;

        #endregion

        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;
        private readonly AgentRuntimeEnum _RuntimeType;
        private readonly int _StartTimeoutMs = 60000;
        private string _JobId = string.Empty;
        private int _ProcessId = 0;
        private TaskCompletionSource<int>? _StartedTcs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific target Harbor and runtime type.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="runtimeType">Agent runtime type.</param>
        public RemoteAgentRuntime(HarborConnectionManager manager, string harborId, AgentRuntimeEnum runtimeType)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
            _RuntimeType = runtimeType;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<int> StartAsync(
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
            _JobId = Guid.NewGuid().ToString("N");
            _StartedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            HarborLaunchRequest request = new HarborLaunchRequest
            {
                JobId = _JobId,
                Runtime = _RuntimeType.ToString(),
                WorkingDirectory = workingDirectory,
                Model = model,
                Prompt = prompt,
                PromptViaStdin = true,
                Arguments = new List<string>(),
                Environment = environment ?? new Dictionary<string, string>()
            };

            await _Manager.LaunchAsync(_HarborId, request, this, token).ConfigureAwait(false);

            Task delay = Task.Delay(_StartTimeoutMs, token);
            Task finished = await Task.WhenAny(_StartedTcs.Task, delay).ConfigureAwait(false);
            if (finished != _StartedTcs.Task)
                throw new TimeoutException("Harbor " + _HarborId + " did not start job " + _JobId + " within the timeout.");

            return await _StartedTcs.Task.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task StopAsync(int processId, CancellationToken token = default)
        {
            await _Manager.KillByProcessIdAsync(processId, 10000, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            return Task.FromResult(_Manager.IsProcessTracked(processId));
        }

        /// <inheritdoc />
        public void OnStarted(int processId)
        {
            _ProcessId = processId;
            _Manager.RegisterProcessId(processId, _HarborId, _JobId);
            OnProcessStarted?.Invoke(processId);
            _StartedTcs?.TrySetResult(processId);
        }

        /// <inheritdoc />
        public void OnOutput(HarborOutputStreamEnum stream, string data)
        {
            OnOutputReceived?.Invoke(_ProcessId, data);
            if (stream == HarborOutputStreamEnum.Stdout)
                OnStdoutReceived?.Invoke(_ProcessId, data);
        }

        /// <inheritdoc />
        public void OnExited(int exitCode)
        {
            _Manager.UnregisterProcessId(_ProcessId);
            OnProcessExited?.Invoke(_ProcessId, exitCode);
        }

        #endregion
    }
}
