namespace Armada.Runtimes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Hosting;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Launches captains on the Harbor host in-process, using the local agent runtimes, and streams their
    /// lifecycle back to the link client. This is the Harbor-side counterpart to the Admiral's
    /// <see cref="RemoteAgentRuntime"/>: the Admiral sends a launch request, this runs the captain here, and
    /// reports started/output/exited over the link.
    /// </summary>
    public class LocalHarborJobRunner : IHarborJobRunner, IHarborJobProgressSource
    {
        #region Private-Members

        private readonly IHostProcessExecutor _Executor;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, JobEntry> _Jobs = new ConcurrentDictionary<string, JobEntry>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _LogPaths = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly string _ScratchRoot;

        // Bound the final-message artifact sent back over the link.
        private const int _MaxFinalMessageChars = 1000000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Where to write each job's output on this machine (the Harbor's jobs log directory), or null to write none.
        /// </summary>
        public HarborLogPaths? JobLogs { get; set; } = null;

        /// <summary>
        /// Where each job's agent process is recorded, so a later Harbor run can stop one that outlived this one; null to
        /// record none.
        /// </summary>
        public HarborProcessLedger? ProcessLedger { get; set; } = null;

        /// <inheritdoc />
        public event Action<string, Armada.Core.Models.RuntimeActivity>? ActivityReported;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public LocalHarborJobRunner(LoggingModule logging)
            : this(logging, new AgentRuntimeFactory(logging ?? throw new ArgumentNullException(nameof(logging))))
        {
        }

        /// <summary>
        /// Instantiate with a specific runtime factory (for example one whose runtimes point at test executables).
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="runtimeFactory">Runtime factory used for CLI runtimes.</param>
        public LocalHarborJobRunner(LoggingModule logging, AgentRuntimeFactory runtimeFactory)
            : this(logging, runtimeFactory, null)
        {
        }

        /// <summary>
        /// Instantiate with a specific runtime factory and scratch root.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="runtimeFactory">Runtime factory used for CLI runtimes.</param>
        /// <param name="scratchRoot">Directory under which per-job scratch directories and final-message files are
        /// created; null uses "armada-harbor" under the system temporary directory.</param>
        public LocalHarborJobRunner(LoggingModule logging, AgentRuntimeFactory runtimeFactory, string? scratchRoot)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (runtimeFactory == null) throw new ArgumentNullException(nameof(runtimeFactory));
            _Executor = new LocalHostProcessExecutor(runtimeFactory);
            _ScratchRoot = String.IsNullOrWhiteSpace(scratchRoot) ? Path.Combine(Path.GetTempPath(), "armada-harbor") : scratchRoot!;
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

            AgentRuntimeEnum? requested = request.RuntimeType;
            if (requested == null)
                throw new NotSupportedException("Unknown runtime requested: " + request.Runtime);
            AgentRuntimeEnum runtimeType = requested.Value;

            IAgentRuntime runtime;
            if (runtimeType == AgentRuntimeEnum.ApiEndpoint)
            {
                // An API-endpoint captain runs its tool-calling loop here on the Harbor, against the endpoint
                // the Admiral shipped in the launch (the Harbor has no database to resolve it).
                if (request.InferenceEndpoint == null)
                    throw new NotSupportedException("API-endpoint captain launch did not include an inference endpoint.");

                Armada.Core.Models.ModelEndpoint endpoint = request.InferenceEndpoint.ToEndpoint();
                runtime = new ApiAgentRuntime(endpoint, _Logging)
                {
                    // A Harbor must enforce the same resolved shell policy as the Admiral.
                    // Approval without a prompt bridge fails closed in ApiAgentRuntime.
                    ShellPolicy = request.CliPermissionPolicy ?? Armada.Core.Enums.CliPermissionPolicyEnum.Refuse
                };
            }
            else
            {
                runtime = _Executor.CreateRuntime(runtimeType);
            }

            string jobId = request.JobId;

            // A mission whose Admiral decodes the structured output itself (StructuredProgress with StreamJsonOutput): run
            // the CLI in its mission structured mode (Claude Code stream-json without partial messages, Codex exec
            // --json, Mux jsonl) and pass every event through unchanged, so the Admiral reads usage and errors from the
            // events. This Harbor decodes the same events for its Running now list and a readable job log.
            bool supportsStructured = MissionStreamDecoder.Supports(runtimeType);
            bool missionPassthrough = request.StructuredProgress && request.StreamJsonOutput && supportsStructured && runtime is BaseAgentRuntime;
            bool runtimeDecodes = false;
            if (missionPassthrough)
            {
                BaseAgentRuntime passthrough = (BaseAgentRuntime)runtime;
                passthrough.StructuredProgress = true;
                passthrough.StructuredPassthrough = true;
            }
            else
            {
                if (runtime is ClaudeCodeRuntime claudeRuntime && request.StreamJsonOutput)
                    claudeRuntime.StreamJsonOutput = true;
                else if (runtime is CodexRuntime codexRuntime && request.StreamJsonOutput)
                    codexRuntime.JsonOutput = true;

                // A mission streaming its progress from an Admiral that expects decoded output: the runtime decodes its
                // structured output into the readable output reported below (as text mode prints it) and activities,
                // which go to the Running now list and the Admiral.
                if (runtime is BaseAgentRuntime structured && request.StructuredProgress)
                {
                    structured.StructuredProgress = true;
                    runtimeDecodes = supportsStructured && !request.StreamJsonOutput;
                }
            }

            // Raw structured events reach the Admiral unchanged (a passthrough mission, an Ask or chat turn that streams
            // JSON, or any Mux launch, whose CLI only speaks jsonl): read them here too, for the Running now list.
            bool observeEvents = supportsStructured && !runtimeDecodes
                && (request.StreamJsonOutput || runtimeType == AgentRuntimeEnum.Mux);

            // Working directory: the requested one, or (for interactive launches that allow it) a per-job scratch
            // directory owned by this Harbor when the request names none or names a path that does not exist here (the
            // Admiral's own paths are not valid on this host).
            string? scratchDirectory = null;
            string workingDirectory = ResolveWorkingDirectory(request)
                ?? throw new ArgumentException("The launch request has no working directory.");
            if (UsesScratchDirectory(request))
            {
                scratchDirectory = workingDirectory;
                Directory.CreateDirectory(scratchDirectory);
            }
            else if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException("The working directory '" + workingDirectory + "' does not exist on this Harbor host.");
            }

            // The final-message artifact is written outside the working directory (never into a dock worktree) and sent
            // back just before the exit.
            string? finalMessageFilePath = null;
            if (request.ReturnFinalMessage)
            {
                string finalDirectory = Path.Combine(_ScratchRoot, "final");
                Directory.CreateDirectory(finalDirectory);
                finalMessageFilePath = Path.Combine(finalDirectory, SafeName(jobId) + ".txt");
            }

            // Keep the job's output on this machine too, so its log can be read here whether or not the Admiral is.
            HarborJobInfo jobInfo = HarborJobInfo.FromLaunch(request, DateTime.UtcNow);
            HarborJobLog? jobLog = null;
            if (JobLogs != null)
            {
                jobLog = HarborJobLog.TryOpen(JobLogs, jobInfo, workingDirectory, out string? logError);
                if (jobLog == null) _Logging.Warn("[LocalHarborJobRunner] could not open the log for job " + jobId + ": " + (logError ?? "unknown error").TrimEnd('.'));
                else _LogPaths[jobId] = jobLog.FilePath;
            }

            // A passthrough mission's job log shows the decoded, readable lines (written by the observer below), not the
            // raw events.
            Action<HarborOutputStreamEnum, string> report = (stream, line) =>
            {
                if (!(missionPassthrough && stream == HarborOutputStreamEnum.Stdout)) jobLog?.WriteOutput(stream, line);
                onOutput(stream, line);
            };

            runtime.OnProcessStarted += processId =>
            {
                _Jobs[jobId] = new JobEntry(runtime, processId);
                ProcessLedger?.Record(jobId, processId);
                onStarted(processId);
            };

            // Report stdout and stderr on their own streams, as a local run raises them: the Admiral's mission lifecycle
            // consumes both, while chat and planning read only stdout (CLI stderr banners stay out of a reply).
            if (runtime is BaseAgentRuntime split)
            {
                split.OnStdoutReceived += (processId, line) => report(HarborOutputStreamEnum.Stdout, line);
                split.OnStderrReceived += (processId, line) => report(HarborOutputStreamEnum.Stderr, line);
            }
            else
            {
                runtime.OnOutputReceived += (processId, line) => report(HarborOutputStreamEnum.Stdout, line);
            }

            if (request.ReturnProviderErrors)
                runtime.OnProviderError += (processId, error) =>
                    report(HarborOutputStreamEnum.ProviderError, System.Text.Json.JsonSerializer.Serialize(error));

            runtime.OnActivity += (processId, activity) =>
            {
                jobLog?.WriteActivity(activity);
                ReportActivity(jobId, activity);
            };

            // Launches whose raw events go to the Admiral unchanged: read the same events here for the Running now list
            // (and, for a passthrough mission, the readable job log); the output reported to the Admiral is not changed.
            MissionStreamDecoder? observer = observeEvents ? MissionStreamDecoder.For(runtimeType) : null;
            object observerLock = new object();
            if (observer != null)
            {
                runtime.OnStdoutReceived += (processId, line) =>
                {
                    MissionStreamDecodeResult observed;
                    lock (observerLock) observed = observer.Decode(line, DateTime.UtcNow);
                    if (missionPassthrough && !observed.Structured) jobLog?.WriteOutput(HarborOutputStreamEnum.Stdout, line);
                    ObserveDecoded(jobId, observed, missionPassthrough ? jobLog : null);
                };
            }

            runtime.OnProcessExited += (processId, exitCode) =>
            {
                if (observer != null)
                {
                    MissionStreamDecodeResult rest;
                    lock (observerLock) rest = observer.Flush(DateTime.UtcNow);
                    ObserveDecoded(jobId, rest, missionPassthrough ? jobLog : null);
                }

                _Jobs.TryRemove(jobId, out JobEntry? _);
                ProcessLedger?.Remove(jobId);
                _LogPaths.TryRemove(jobId, out string? _);
                SendFinalMessage(finalMessageFilePath, report);
                TryDeleteDirectory(scratchDirectory);
                jobLog?.WriteExit(exitCode ?? -1, DateTime.UtcNow, jobInfo.StartedUtc);
                jobLog?.Dispose();
                onExited(exitCode ?? -1);
            };

            _Logging.Info("[LocalHarborJobRunner] launching " + runtimeType + " for job " + jobId + " in " + workingDirectory);

            // Rebuild the captain the Admiral launched from what the request carries: its runtime options (a Mux
            // captain's endpoint, config directory, and so on), its reasoning effort, and the auto-approve decision the
            // Admiral resolved (captain setting plus vessel override), applied on top. The Harbor has no database, so
            // all of it travels in the launch request; fields an older Admiral does not send keep the defaults.
            Armada.Core.Models.Captain? launchCaptain = null;
            if (request.AutoApprove.HasValue || !String.IsNullOrWhiteSpace(request.RuntimeOptionsJson) || request.ReasoningEffort.HasValue)
            {
                launchCaptain = new Armada.Core.Models.Captain("harbor-job-" + jobId);
                launchCaptain.Runtime = runtimeType;
                launchCaptain.RuntimeOptionsJson = request.AutoApprove.HasValue
                    ? CaptainRuntimeOptions.WithAutoApprove(request.RuntimeOptionsJson, request.AutoApprove.Value)
                    : request.RuntimeOptionsJson;
                launchCaptain.ReasoningEffort = request.ReasoningEffort;
            }

            // Bind the captain's Armada MCP connection to the mission-scoped token the Admiral shipped (O-04): the API
            // endpoint runtime reads ARMADA_MCP_URL / ARMADA_MCP_TOKEN; CLI runtimes get the same per-invocation binding
            // as a local mission launch, against the MCP URL the Admiral advertised in the handshake.
            Dictionary<string, string> environment = new Dictionary<string, string>(request.Environment ?? new Dictionary<string, string>());
            // A launch without a scoped binding must not inherit the Harbor process's credentials.
            environment[CaptainThreadMcpPlanner.TokenEnvironmentVariable] = String.Empty;
            environment["ARMADA_MCP_URL"] = String.Empty;
            bool bindMcp = false;
            int mcpPort = 0;
            if (!String.IsNullOrEmpty(request.McpSessionToken) && !String.IsNullOrWhiteSpace(mcpBaseUrl))
            {
                environment[CaptainThreadMcpPlanner.TokenEnvironmentVariable] = request.McpSessionToken!;
                environment["ARMADA_MCP_URL"] = mcpBaseUrl!;
                if (runtime is BaseAgentRuntime hosted)
                {
                    if (ArmadaMcpConfigBuilder.TryParsePlainMcpUrl(mcpBaseUrl, out string mcpHost, out int parsedPort))
                    {
                        hosted.McpSessionToken = request.McpSessionToken;
                        hosted.McpHost = mcpHost;
                        hosted.McpAllowWorkingDirectoryFiles = false;
                        bindMcp = true;
                        mcpPort = parsedPort;
                    }
                    else
                    {
                        _Logging.Warn("[LocalHarborJobRunner] advertised MCP URL " + mcpBaseUrl + " is not a plain http://host:port/mcp URL; job " + jobId + " runs without a per-launch MCP binding");
                    }
                }
            }

            // Approve in Armada: the CLI's permission prompts go to Armada's prompt tool on the scoped MCP server bound above.
            // Without that binding the tool is unreachable, so the launch stays as the auto-approve option says (Refuse).
            if (request.CliPermissionPolicy == Armada.Core.Enums.CliPermissionPolicyEnum.ApproveInArmada)
            {
                bool prompts = bindMcp && CliPermissionPromptSetup.Configure(runtime, Armada.Core.Enums.CliPermissionPolicyEnum.ApproveInArmada, request.PermissionPromptTimeoutSeconds ?? 600);
                string note = prompts
                    ? "permission prompts go to Armada (Approve in Armada)"
                    : "Approve in Armada runs as Refuse here (" + (bindMcp ? "this host's " + runtimeType + " cannot send permission prompts" : "no scoped MCP binding") + ")";
                _Logging.Info("[LocalHarborJobRunner] job " + jobId + ": " + note);
                jobLog?.WriteNote(note);
            }

            if (request.CliPermissionPolicy.HasValue && request.CliPermissionPolicy != Armada.Core.Enums.CliPermissionPolicyEnum.ApproveInArmada)
                CliPermissionPromptSetup.Configure(runtime, request.CliPermissionPolicy.Value, request.PermissionPromptTimeoutSeconds ?? 600);

            try
            {
                await runtime.StartAsync(
                    workingDirectory,
                    request.Prompt ?? string.Empty,
                    environment,
                    finalMessageFilePath: finalMessageFilePath,
                    model: request.Model,
                    captain: launchCaptain,
                    isolateLaunch: bindMcp,
                    mcpPort: mcpPort,
                    showThinking: request.ShowThinking,
                    token: token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _LogPaths.TryRemove(jobId, out string? _);
                jobLog?.WriteNote("launch failed: " + ex.Message);
                jobLog?.Dispose();
                TryDeleteDirectory(scratchDirectory);
                TryDeleteFile(finalMessageFilePath);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            if (_Jobs.TryRemove(jobId, out JobEntry? entry))
                await entry!.Runtime.StopAsync(entry.ProcessId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string? LogPathOf(string jobId)
        {
            if (String.IsNullOrEmpty(jobId)) return null;
            return _LogPaths.TryGetValue(jobId, out string? path) ? path : null;
        }

        /// <inheritdoc />
        public string? ResolveWorkingDirectory(HarborLaunchRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (UsesScratchDirectory(request)) return Path.Combine(_ScratchRoot, "scratch", SafeName(request.JobId));
            if (String.IsNullOrWhiteSpace(request.WorkingDirectory)) return null;
            return request.WorkingDirectory;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Whether a launch runs in a per-job scratch directory: the request allows one and names no directory that
        /// exists on this host (the Admiral's own paths are not valid here).
        /// </summary>
        private static bool UsesScratchDirectory(HarborLaunchRequest request)
        {
            return request.ScratchWorkingDirectory
                && (String.IsNullOrWhiteSpace(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory));
        }

        private void SendFinalMessage(string? finalMessageFilePath, Action<HarborOutputStreamEnum, string> onOutput)
        {
            if (finalMessageFilePath == null) return;
            try
            {
                if (File.Exists(finalMessageFilePath))
                {
                    string text = File.ReadAllText(finalMessageFilePath);
                    if (text.Length > _MaxFinalMessageChars) text = text.Substring(0, _MaxFinalMessageChars);
                    if (!String.IsNullOrWhiteSpace(text)) onOutput(HarborOutputStreamEnum.FinalMessage, text);
                }
            }
            catch (Exception e)
            {
                _Logging.Warn("[LocalHarborJobRunner] could not read the final message " + finalMessageFilePath + ": " + e.Message);
            }
            finally
            {
                TryDeleteFile(finalMessageFilePath);
            }
        }

        private void ObserveDecoded(string jobId, MissionStreamDecodeResult decoded, HarborJobLog? jobLog)
        {
            foreach (Armada.Core.Models.RuntimeActivity activity in decoded.Activities)
            {
                jobLog?.WriteActivity(activity);
                ReportActivity(jobId, activity);
            }

            if (jobLog == null) return;
            foreach (string line in decoded.TranscriptLines) jobLog.WriteOutput(HarborOutputStreamEnum.Stderr, line);
            foreach (string line in decoded.OutputLines) jobLog.WriteOutput(HarborOutputStreamEnum.Stdout, line);
        }

        private void ReportActivity(string jobId, Armada.Core.Models.RuntimeActivity activity)
        {
            try { ActivityReported?.Invoke(jobId, activity); }
            catch (Exception e) { _Logging.Debug("[LocalHarborJobRunner] activity of job " + jobId + " not reported: " + e.Message.TrimEnd('.')); }
        }

        private static string SafeName(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return Guid.NewGuid().ToString("N");
            char[] chars = jobId.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!Char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            }
            return new string(chars);
        }

        private static void TryDeleteDirectory(string? path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private static void TryDeleteFile(string? path)
        {
            if (String.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
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
