namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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

        /// <summary>
        /// Whether the runtime type can run planning sessions; a Harbor runs the same CLI, so the answer is the runtime
        /// type's (see <see cref="AgentRuntimeCapabilities.SupportsPlanningSessions"/>).
        /// </summary>
        public bool SupportsPlanningSessions => AgentRuntimeCapabilities.SupportsPlanningSessions(_RuntimeType);

        /// <inheritdoc />
        public event Action<int, string>? OnOutputReceived;

        /// <inheritdoc />
        public event Action<int, string>? OnStdoutReceived;

        /// <inheritdoc />
        public event Action<int>? OnProcessStarted;

        /// <inheritdoc />
        public event Action<int, int?>? OnProcessExited;

        /// <inheritdoc />
        public event Action<int, RuntimeProviderError>? OnProviderError;

        /// <inheritdoc />
        public event Action<int, RuntimeActivity>? OnActivity;

        /// <summary>
        /// Mission-scoped MCP session token to ship with the launch so the Harbor binds the captain's Armada MCP
        /// connection to it, or null.
        /// </summary>
        public string? McpSessionToken { get; set; } = null;

        /// <summary>
        /// Whether the launch may run in a Harbor-owned scratch directory when its working directory is empty or does
        /// not exist on the Harbor host (see <see cref="HarborLaunchRequest.ScratchWorkingDirectory"/>). Interactive
        /// launches (chat, planning, refinement) set it; missions leave it false.
        /// </summary>
        public bool UseScratchWorkingDirectory { get; set; } = false;

        /// <summary>
        /// Whether a Claude Code captain runs in streaming-JSON output mode, or a Codex captain in 'codex exec --json'
        /// mode, on the Harbor (chat turns). Other runtimes ignore it.
        /// </summary>
        public bool StreamJsonOutput { get; set; } = false;

        /// <summary>
        /// What the launch is for, sent to the Harbor for its job list and logs. Informational.
        /// </summary>
        public HarborJobKindEnum JobKind { get; set; } = HarborJobKindEnum.Other;

        /// <summary>
        /// The mission the launch runs, when it is a mission; sent to the Harbor for its job list and logs.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Whether a mission runs with its runtime's structured output on the Harbor (see
        /// <see cref="HarborLaunchRequest.StructuredProgress"/>): the Harbor reports readable output as before and the
        /// captain's activity, raised on <see cref="OnActivity"/>. Sent only for runtimes that have structured output.
        /// </summary>
        public bool StructuredProgress { get; set; } = false;

        /// <summary>
        /// What the launch is about, for the Harbor's Running now list (see <see cref="HarborLaunchRequest.Display"/>), or
        /// null.
        /// </summary>
        public HarborLaunchDisplay? Display { get; set; } = null;

        /// <summary>
        /// The Harbor this runtime launches on.
        /// </summary>
        public string HarborId => _HarborId;

        #endregion

        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;
        private readonly AgentRuntimeEnum _RuntimeType;
        private readonly Func<string, ModelEndpoint?>? _EndpointResolver;
        private readonly int _StartTimeoutMs = 60000;
        private string _JobId = string.Empty;
        private int _ProcessId = 0;
        private TaskCompletionSource<int>? _StartedTcs;
        private StreamWriter? _LogWriter;
        private readonly object _LogLock = new object();
        private string? _FinalMessageFilePath = null;
        private int _Finished = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific target Harbor and runtime type.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Target Harbor identifier.</param>
        /// <param name="runtimeType">Agent runtime type.</param>
        /// <param name="endpointResolver">Optional resolver mapping a captain's model-endpoint id to a
        /// ModelEndpoint, used to ship the endpoint to the Harbor for API-endpoint captains.</param>
        public RemoteAgentRuntime(HarborConnectionManager manager, string harborId, AgentRuntimeEnum runtimeType, Func<string, ModelEndpoint?>? endpointResolver = null)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
            _EndpointResolver = endpointResolver;
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
            if (String.IsNullOrEmpty(workingDirectory) && !UseScratchWorkingDirectory) throw new ArgumentNullException(nameof(workingDirectory));
            _JobId = Guid.NewGuid().ToString("N");
            _StartedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _FinalMessageFilePath = String.IsNullOrEmpty(finalMessageFilePath) ? null : finalMessageFilePath;
            Interlocked.Exchange(ref _Finished, 0);

            // Mirror the captain's streamed output into the Admiral-side mission log file so live-follow
            // (which tails this file) and the stored log read identically to a local run. The captain process
            // itself runs on the Harbor, so this is the only server-side copy of its transcript.
            OpenLog(logFilePath, prompt);

            HarborLaunchRequest request = new HarborLaunchRequest
            {
                JobId = _JobId,
                Runtime = _RuntimeType.ToString(),
                WorkingDirectory = workingDirectory ?? String.Empty,
                Model = model,
                Prompt = prompt,
                PromptViaStdin = true,
                Arguments = new List<string>(),
                Environment = environment ?? new Dictionary<string, string>(),
                AutoApprove = captain != null ? CaptainRuntimeOptions.GetAutoApprove(captain) : (bool?)null,
                McpSessionToken = String.IsNullOrEmpty(McpSessionToken) ? null : McpSessionToken,
                ScratchWorkingDirectory = UseScratchWorkingDirectory,
                StreamJsonOutput = StreamJsonOutput && (_RuntimeType == AgentRuntimeEnum.ClaudeCode || _RuntimeType == AgentRuntimeEnum.Codex),
                ShowThinking = showThinking,
                ReturnFinalMessage = _FinalMessageFilePath != null,
                JobKind = JobKind.ToString(),
                MissionId = String.IsNullOrWhiteSpace(MissionId) ? null : MissionId,
                CaptainId = captain != null && !String.IsNullOrWhiteSpace(captain.Id) ? captain.Id : null,
                StructuredProgress = StructuredProgress && MissionStreamDecoder.Supports(_RuntimeType),
                Display = Display != null && !Display.IsEmpty() ? Display.Clone() : null
            };

            // API-endpoint captains have no CLI on the Harbor; ship the resolved endpoint so the Harbor can
            // drive it. The Harbor has no database to resolve it itself.
            if (_RuntimeType == AgentRuntimeEnum.ApiEndpoint)
            {
                ModelEndpoint? endpoint = (_EndpointResolver != null && captain != null && !String.IsNullOrEmpty(captain.ModelEndpointId))
                    ? _EndpointResolver(captain.ModelEndpointId)
                    : null;
                if (endpoint == null)
                    throw new InvalidOperationException("API-endpoint captain has no resolvable inference endpoint to delegate to the Harbor.");
                request.InferenceEndpoint = new HarborInferenceEndpoint
                {
                    Name = endpoint.Name,
                    Provider = endpoint.Provider,
                    Kind = endpoint.Kind,
                    BaseUrl = endpoint.BaseUrl ?? string.Empty,
                    Model = endpoint.Model,
                    ApiKey = endpoint.ApiKey,
                    TimeoutMs = endpoint.TimeoutMs
                };
            }

            await _Manager.LaunchAsync(_HarborId, request, this, token).ConfigureAwait(false);

            using (CancellationTokenSource waitCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task delay = Task.Delay(_StartTimeoutMs, waitCts.Token);
                Task finished = await Task.WhenAny(_StartedTcs.Task, delay).ConfigureAwait(false);
                waitCts.Cancel();
                if (finished != _StartedTcs.Task)
                {
                    // The caller gave up (or the Harbor never answered): make sure the job does not start later unowned.
                    try { await _Manager.KillJobAsync(_HarborId, _JobId, 10000, CancellationToken.None).ConfigureAwait(false); }
                    catch { }
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    throw new TimeoutException("Harbor " + _HarborId + " did not start job " + _JobId + " within the timeout.");
                }
            }

            return await _StartedTcs.Task.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task StopAsync(int processId, CancellationToken token = default)
        {
            // This instance launched the job: stop it by its job id, which cannot collide with another Harbor's process
            // id. Otherwise (a runtime created only to stop a process) resolve the job from the process id.
            if (!String.IsNullOrEmpty(_JobId) && processId == _ProcessId && _ProcessId != 0)
            {
                _Manager.UnregisterProcessId(processId);
                try
                {
                    await _Manager.KillJobAsync(_HarborId, _JobId, 10000, token).ConfigureAwait(false);
                }
                finally
                {
                    // The kill drops this job's listener, so the Harbor's own exit report never arrives: report the exit
                    // here, as a local process kill would, so a caller waiting for it (a planning turn) finishes.
                    OnExited(-1);
                }
                return;
            }

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
            if (stream == HarborOutputStreamEnum.FinalMessage)
            {
                // The runtime's final-message artifact, captured on the Harbor: write it where a local run would have,
                // so callers read it the same way. It is not captain output.
                WriteFinalMessage(data);
                return;
            }

            WriteLog(stream == HarborOutputStreamEnum.Stderr ? "[stderr] " + data : data);
            OnOutputReceived?.Invoke(_ProcessId, data);
            if (stream == HarborOutputStreamEnum.Stdout)
                OnStdoutReceived?.Invoke(_ProcessId, data);

            // A harbor runs Claude Code missions in text mode, so the CLI's own whole-line protocol errors are the
            // structured error channel here as well.
            if (_RuntimeType == AgentRuntimeEnum.ClaudeCode)
            {
                RuntimeProviderError? error = RuntimeProviderErrorParser.TryParseClaudeTextLine(data);
                if (error != null)
                {
                    try { OnProviderError?.Invoke(_ProcessId, error); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// The Harbor reported the job's latest activity: mirror it into the mission log and raise <see cref="OnActivity"/>.
        /// Implemented explicitly because the runtime's <see cref="OnActivity"/> event has the same name.
        /// </summary>
        /// <param name="activity">The activity.</param>
        void IHarborJobListener.OnActivity(RuntimeActivity activity)
        {
            if (activity == null) return;
            WriteLog("> " + activity.Summary);
            try { OnActivity?.Invoke(_ProcessId, activity); }
            catch { }
        }

        /// <inheritdoc />
        public void OnExited(int exitCode)
        {
            if (Interlocked.Exchange(ref _Finished, 1) == 1) return;
            _Manager.UnregisterProcessId(_ProcessId);
            CloseLog();
            OnProcessExited?.Invoke(_ProcessId, exitCode);
        }

        /// <inheritdoc />
        public void OnFailed(string message)
        {
            HarborJobFailedException failure = new HarborJobFailedException(_HarborId, _JobId, message);
            WriteLog("Harbor " + _HarborId + " reported a failure: " + message);
            if (_StartedTcs != null && _StartedTcs.TrySetException(failure))
            {
                CloseLog();
                return;
            }

            // The job had already started: report it as ended so nobody waits for an exit that will never come.
            OnExited(-1);
        }

        #endregion

        #region Private-Methods

        private void OpenLog(string? logFilePath, string prompt)
        {
            if (String.IsNullOrEmpty(logFilePath)) return;
            try
            {
                lock (_LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                    _LogWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    _LogWriter.WriteLine("[" + timestamp + "] Agent starting on Harbor " + _HarborId + " (" + _RuntimeType + ")");
                    if (!String.IsNullOrEmpty(prompt)) _LogWriter.WriteLine(prompt);
                    _LogWriter.WriteLine(String.Empty);
                }
            }
            catch
            {
                // A logging failure must never block the launch; live-follow simply shows less.
                _LogWriter = null;
            }
        }

        private void WriteFinalMessage(string data)
        {
            if (_FinalMessageFilePath == null) return;
            try
            {
                string? directory = Path.GetDirectoryName(_FinalMessageFilePath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_FinalMessageFilePath, data ?? String.Empty);
            }
            catch
            {
                // The final message is optional; callers fall back to the streamed output.
            }
        }

        private void WriteLog(string data)
        {
            lock (_LogLock)
            {
                try { _LogWriter?.WriteLine(data); }
                catch (ObjectDisposedException) { }
                catch { }
            }
        }

        private void CloseLog()
        {
            lock (_LogLock)
            {
                try { _LogWriter?.Flush(); } catch { }
                try { _LogWriter?.Dispose(); } catch { }
                _LogWriter = null;
            }
        }

        #endregion
    }
}
