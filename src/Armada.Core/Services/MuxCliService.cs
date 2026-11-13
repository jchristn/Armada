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
        #region Public-Members

        /// <summary>
        /// Oldest mux structured output contract Armada reads.
        /// </summary>
        public const int MinimumContractVersion = 1;

        /// <summary>
        /// Newest mux structured output contract Armada reads. Version 2 only adds fields (the token usage block on the
        /// run summary and the run_completed event, and the --no-stats switch), so the version 1 parser reads it.
        /// </summary>
        public const int MaximumContractVersion = 2;

        #endregion

        #region Private-Members

        private readonly string _Header = "[MuxCliService] ";
        private readonly LoggingModule _Logging;
        private readonly IHostCommandExecutor _CommandExecutor;
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly TimeSpan _DefaultTimeout = TimeSpan.FromSeconds(10);
        // Probe performs real inference; cold local model loading and generation need more time than metadata reads.
        private readonly TimeSpan _ProbeTimeout = TimeSpan.FromSeconds(120);
        private const string MuxNotFoundMessage = "mux could not be started: it is not installed or not on the PATH of the machine that ran it. "
            + "Mux captains run on a Harbor; start Armada Harbor on the machine where mux is installed (and make sure mux is on that Harbor's PATH).";

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
                _ProbeTimeout,
                workingDirectory,
                "Mux probe of endpoint " + options.Endpoint,
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
                "Mux endpoint list",
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
        /// One page of the configured Mux endpoints, ordered by name (case-insensitive, then ordinal). mux reads its whole
        /// configuration file and cannot page, so the page is cut here from its answer; TotalRecords counts every saved
        /// endpoint. Each endpoint's header lists are checked against their declared bounds.
        /// </summary>
        /// <param name="configDirectory">Mux config directory override, or null for the default.</param>
        /// <param name="query">Paging (PageNumber, PageSize 1 to 1000, default 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page; Success is false with ErrorCode and ErrorMessage when mux failed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when an endpoint reports more headers than allowed.</exception>
        public async Task<MuxEndpointPage> EnumerateEndpointsAsync(string? configDirectory, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            MuxEndpointListResult list = await ListEndpointsAsync(configDirectory, token).ConfigureAwait(false);
            return ToPage(list, query);
        }

        /// <summary>
        /// Cut one page from a mux endpoint list, ordered by name.
        /// </summary>
        /// <param name="list">The list mux returned.</param>
        /// <param name="query">Paging.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when an endpoint reports more headers than allowed.</exception>
        public static MuxEndpointPage ToPage(MuxEndpointListResult list, EnumerationQuery query)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<MuxEndpointInfo> ordered = (list.Endpoints ?? new List<MuxEndpointInfo>())
                .Where(e => e != null)
                .OrderBy(e => e.Name ?? String.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name ?? String.Empty, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < ordered.Count; i++) CollectionBounds.Validate(ordered[i], "Objects[" + i + "]");

            EnumerationResult<MuxEndpointInfo> page = CollectionLimits.Page(query, ordered);
            return new MuxEndpointPage
            {
                Success = list.Success,
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalPages = page.TotalPages,
                TotalRecords = page.TotalRecords,
                Objects = page.Objects,
                ContractVersion = list.ContractVersion,
                HarborId = list.HarborId,
                ConfigDirectory = list.ConfigDirectory ?? String.Empty,
                ErrorCode = list.ErrorCode ?? String.Empty,
                ErrorMessage = list.ErrorMessage ?? String.Empty
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
                "Mux endpoint " + endpointName,
                token).ConfigureAwait(false);

            MuxEndpointShowResult? result = DeserializeJson<MuxEndpointShowResult>(execution.Stdout, execution.Stderr);
            if (result != null)
            {
                if (result.Endpoint != null) CollectionBounds.Validate(result.Endpoint, "Endpoint");
                return result;
            }

            return new MuxEndpointShowResult
            {
                Success = false,
                ErrorCode = execution.ExitCode == 0 ? "invalid_json" : "endpoint_show_error",
                ErrorMessage = BuildInvalidJsonMessage("endpoint show", execution)
            };
        }

        /// <summary>
        /// Whether Armada reads a mux structured output contract version.
        /// </summary>
        /// <param name="contractVersion">Version mux reported.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsContractVersion(int contractVersion)
        {
            return contractVersion >= MinimumContractVersion && contractVersion <= MaximumContractVersion;
        }

        #endregion


        #region Private-Methods

        private async Task<MuxCommandExecutionResult> ExecuteAsync(
            List<string> arguments,
            TimeSpan timeout,
            string? workingDirectory,
            string label,
            CancellationToken token)
        {
            // A labeled Command (not routine git work) so a Harbor's log lists it at summary level as, for example,
            // "Mux endpoint list: mux endpoint list ... -> exit 0".
            HostCommandRequest request = new HostCommandRequest
            {
                Executable = "mux",
                Arguments = arguments,
                WorkingDirectory = String.IsNullOrWhiteSpace(workingDirectory) ? String.Empty : workingDirectory!,
                TimeoutMs = (int)timeout.TotalMilliseconds,
                Kind = HostCommandKindEnum.Command,
                Label = label
            };

            HostCommandResult result;
            try
            {
                result = await _CommandExecutor.RunAsync(request, token).ConfigureAwait(false);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(MuxNotFoundMessage, ex);
            }

            // A Harbor reports a process that could not start as exit code -1 with the start error on stderr.
            if (result.ExitCode == -1 && (result.StandardError ?? String.Empty).IndexOf("trying to start process", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(MuxNotFoundMessage);
            }

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

        /// <summary>
        /// Return the stream text when it is a single JSON object and nothing else. Mux writes its machine-readable
        /// output as the whole of the stream, so text with a banner or log lines around a brace pair is not accepted.
        /// </summary>
        private static string? ExtractJsonPayload(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string trimmed = text.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}')
            {
                return trimmed;
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
