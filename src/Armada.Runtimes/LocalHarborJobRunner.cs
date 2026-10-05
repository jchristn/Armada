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
            : this(logging, new AgentRuntimeFactory(logging ?? throw new ArgumentNullException(nameof(logging))))
        {
        }

        /// <summary>
        /// Instantiate with a specific runtime factory (for example one whose runtimes point at test executables).
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="runtimeFactory">Runtime factory used for CLI runtimes.</param>
        public LocalHarborJobRunner(LoggingModule logging, AgentRuntimeFactory runtimeFactory)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (runtimeFactory == null) throw new ArgumentNullException(nameof(runtimeFactory));
            _Executor = new LocalHostProcessExecutor(runtimeFactory);
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

            await runtime.StartAsync(
                request.WorkingDirectory,
                request.Prompt ?? string.Empty,
                environment,
                model: request.Model,
                captain: launchCaptain,
                isolateLaunch: bindMcp,
                mcpPort: mcpPort,
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
