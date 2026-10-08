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
    public class LocalHarborJobRunner : IHarborJobRunner
    {
        #region Private-Members

        private readonly IHostProcessExecutor _Executor;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, JobEntry> _Jobs = new ConcurrentDictionary<string, JobEntry>(StringComparer.Ordinal);
        private readonly string _ScratchRoot;

        // Bound the final-message artifact sent back over the link.
        private const int _MaxFinalMessageChars = 1000000;

        #endregion

        #region Public-Members

        /// <summary>
        /// Where to write each job's output on this machine (the Harbor's jobs log directory), or null to write none.
        /// </summary>
        public HarborLogPaths? JobLogs { get; set; } = null;

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

                HarborInferenceEndpoint shipped = request.InferenceEndpoint;
                Armada.Core.Models.ModelEndpoint endpoint = new Armada.Core.Models.ModelEndpoint
                {
                    Name = shipped.Name,
                    Provider = shipped.Provider,
                    Kind = shipped.Kind,
                    BaseUrl = shipped.BaseUrl,
                    Model = shipped.Model,
                    TimeoutMs = shipped.TimeoutMs
                };
                endpoint.ApiKey = shipped.ApiKey;
                runtime = new ApiAgentRuntime(endpoint, _Logging);
            }
            else
            {
                runtime = _Executor.CreateRuntime(runtimeType);
            }

            string jobId = request.JobId;
            if (runtime is ClaudeCodeRuntime claudeRuntime && request.StreamJsonOutput)
                claudeRuntime.StreamJsonOutput = true;

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
            }

            Action<HarborOutputStreamEnum, string> report = (stream, line) =>
            {
                jobLog?.WriteOutput(stream, line);
                onOutput(stream, line);
            };

            runtime.OnProcessStarted += processId =>
            {
                _Jobs[jobId] = new JobEntry(runtime, processId);
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

            runtime.OnProcessExited += (processId, exitCode) =>
            {
                _Jobs.TryRemove(jobId, out JobEntry? _);
                SendFinalMessage(finalMessageFilePath, report);
                TryDeleteDirectory(scratchDirectory);
                jobLog?.WriteExit(exitCode ?? -1, DateTime.UtcNow, jobInfo.StartedUtc);
                jobLog?.Dispose();
                onExited(exitCode ?? -1);
            };

            _Logging.Info("[LocalHarborJobRunner] launching " + runtimeType + " for job " + jobId + " in " + workingDirectory);

            // Apply the auto-approve decision the Admiral resolved (captain setting plus vessel override). The Harbor has
            // no database, so the decision travels in the launch request; null (an older Admiral) keeps the default.
            Armada.Core.Models.Captain? launchCaptain = null;
            if (request.AutoApprove.HasValue)
            {
                launchCaptain = new Armada.Core.Models.Captain("harbor-job-" + jobId);
                launchCaptain.RuntimeOptionsJson = CaptainRuntimeOptions.WithAutoApprove(null, request.AutoApprove.Value);
            }

            // Bind the captain's Armada MCP connection to the mission-scoped token the Admiral shipped (O-04): the API
            // endpoint runtime reads ARMADA_MCP_URL / ARMADA_MCP_TOKEN; CLI runtimes get the same per-invocation binding
            // as a local mission launch, against the MCP URL the Admiral advertised in the handshake.
            Dictionary<string, string> environment = new Dictionary<string, string>(request.Environment ?? new Dictionary<string, string>());
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
