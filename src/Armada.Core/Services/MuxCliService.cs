namespace Armada.Core.Services
{
    using System.Text.Json;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Executes Mux CLI commands used by Armada for validation and endpoint inspection. Every command runs
    /// through an <see cref="IHostCommandExecutor"/>: in standalone mode that is the local host, but when a
    /// captain runs on a Harbor the caller can supply a remote executor so the probe runs on the Harbor host
    /// where mux, its config, and its provider auth actually live rather than on the Admiral.
    /// </summary>
    public class MuxCliService
    {
        #region Private-Members

        private readonly string _Header = "[MuxCliService] ";
        private readonly LoggingModule _Logging;
        private readonly IHostCommandExecutor _CommandExecutor;
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly TimeSpan _DefaultTimeout = TimeSpan.FromSeconds(10);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module. Required.</param>
        /// <param name="commandExecutor">Host-command executor used to run mux. When null, mux runs on the
        /// local host; pass a <see cref="RemoteHostCommandExecutor"/> to run it on a specific Harbor.</param>
        public MuxCliService(LoggingModule logging, IHostCommandExecutor? commandExecutor = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _CommandExecutor = commandExecutor ?? new LocalHostCommandExecutor();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe a Mux captain configuration on the local host.
        /// </summary>
        public async Task<MuxProbeResult> ProbeAsync(Captain captain, CancellationToken token = default)
        {
            return await ProbeAsync(captain, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Probe a Mux captain configuration, optionally in a specific working directory (the captain's dock
        /// worktree). The command runs through the configured executor, so when that executor targets a Harbor
        /// the probe reflects the Harbor host rather than the Admiral.
        /// </summary>
        public async Task<MuxProbeResult> ProbeAsync(Captain captain, string? workingDirectory, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            MuxCaptainOptions? options = CaptainRuntimeOptions.GetMuxOptions(captain);
            if (options == null)
            {
                return new MuxProbeResult
                {
                    Success = false,
                    ErrorCode = "config_error",
                    FailureCategory = "configuration",
                    ErrorMessage = "Mux captains require runtime options with at least an endpoint selection."
                };
            }

            return await ProbeAsync(captain.Model, options, workingDirectory, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Probe a Mux endpoint selection directly.
        /// </summary>
        public async Task<MuxProbeResult> ProbeAsync(string? model, MuxCaptainOptions options, string? workingDirectory = null, CancellationToken token = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            MuxCommandExecutionResult execution = await ExecuteAsync(
                MuxCommandBuilder.BuildProbeArguments(model, options),
                _DefaultTimeout,
                workingDirectory,
                token).ConfigureAwait(false);

            MuxProbeResult? result = DeserializeJson<MuxProbeResult>(execution.Stdout, execution.Stderr);
            if (result != null)
            {
                return result;
            }

            return new MuxProbeResult
            {
                Success = false,
                ErrorCode = execution.ExitCode == 0 ? "invalid_json" : "probe_error",
                FailureCategory = execution.ExitCode == 0 ? "parsing" : "unknown",
                ErrorMessage = BuildInvalidJsonMessage("probe", execution)
            };
        }

        /// <summary>
        /// Enumerate configured Mux endpoints.
        /// </summary>
        public async Task<MuxEndpointListResult> ListEndpointsAsync(string? configDirectory, CancellationToken token = default)
        {
            MuxCommandExecutionResult execution = await ExecuteAsync(
                MuxCommandBuilder.BuildEndpointListArguments(configDirectory),
                _DefaultTimeout,
                null,
                token).ConfigureAwait(false);

            MuxEndpointListResult? result = DeserializeJson<MuxEndpointListResult>(execution.Stdout, execution.Stderr);
            if (result != null)
            {
                return result;
            }

            return new MuxEndpointListResult
            {
                Success = false,
                ErrorCode = execution.ExitCode == 0 ? "invalid_json" : "endpoint_list_error",
                ErrorMessage = BuildInvalidJsonMessage("endpoint list", execution)
            };
        }

        /// <summary>
        /// Inspect a single configured Mux endpoint.
        /// </summary>
        public async Task<MuxEndpointShowResult> ShowEndpointAsync(string endpointName, string? configDirectory, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(endpointName)) throw new ArgumentNullException(nameof(endpointName));

            MuxCommandExecutionResult execution = await ExecuteAsync(
                MuxCommandBuilder.BuildEndpointShowArguments(endpointName, configDirectory),
                _DefaultTimeout,
                null,
                token).ConfigureAwait(false);

            MuxEndpointShowResult? result = DeserializeJson<MuxEndpointShowResult>(execution.Stdout, execution.Stderr);
            if (result != null)
            {
                return result;
            }

            return new MuxEndpointShowResult
            {
                Success = false,
                ErrorCode = execution.ExitCode == 0 ? "invalid_json" : "endpoint_show_error",
                ErrorMessage = BuildInvalidJsonMessage("endpoint show", execution)
            };
        }

        #endregion

        #region Private-Methods

        private async Task<MuxCommandExecutionResult> ExecuteAsync(
            List<string> arguments,
            TimeSpan timeout,
            string? workingDirectory,
            CancellationToken token)
        {
            HostCommandRequest request = new HostCommandRequest
            {
                Executable = "mux",
                Arguments = arguments,
                WorkingDirectory = String.IsNullOrWhiteSpace(workingDirectory) ? String.Empty : workingDirectory!,
                TimeoutMs = (int)timeout.TotalMilliseconds
            };

            HostCommandResult result = await _CommandExecutor.RunAsync(request, token).ConfigureAwait(false);

            if (result.TimedOut)
            {
                throw new TimeoutException("mux command timed out after " + timeout.TotalSeconds.ToString("0") + " seconds.");
            }

            string stdout = result.StandardOutput ?? String.Empty;
            string stderr = result.StandardError ?? String.Empty;

            if (result.ExitCode != 0)
            {
                _Logging.Debug(_Header + "mux exited with code " + result.ExitCode + ": " + FirstNonEmptyLine(stderr, stdout));
            }

            return new MuxCommandExecutionResult
            {
                ExitCode = result.ExitCode,
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim()
            };
        }

        private T? DeserializeJson<T>(string stdout, string stderr) where T : class
        {
            string? json = ExtractJsonPayload(stdout);
            if (String.IsNullOrWhiteSpace(json))
            {
                json = ExtractJsonPayload(stderr);
            }

            if (String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, _JsonOptions);
            }
            catch (JsonException ex)
            {
                _Logging.Warn(_Header + "could not parse mux JSON payload: " + ex.ToString());
                return null;
            }
        }

        private static string? ExtractJsonPayload(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return null;
        }

        private static string BuildInvalidJsonMessage(string commandName, MuxCommandExecutionResult execution)
        {
            string details = FirstNonEmptyLine(execution.Stderr, execution.Stdout);
            if (String.IsNullOrWhiteSpace(details))
            {
                details = "mux returned exit code " + execution.ExitCode + ".";
            }

            return "Mux " + commandName + " returned an unreadable response. " + details;
        }

        private static string FirstNonEmptyLine(string? primary, string? secondary)
        {
            foreach (string source in new[] { primary ?? String.Empty, secondary ?? String.Empty })
            {
                foreach (string line in source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    if (!String.IsNullOrWhiteSpace(trimmed))
                    {
                        return trimmed;
                    }
                }
            }

            return String.Empty;
        }

        private sealed class MuxCommandExecutionResult
        {
            public int ExitCode { get; set; } = 0;
            public string Stdout { get; set; } = String.Empty;
            public string Stderr { get; set; } = String.Empty;
        }

        #endregion
    }
}
