namespace Armada.Core.Services
{
    using System.Diagnostics;
    using System.Text.Json;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Executes Mux CLI commands used by Armada for validation and endpoint inspection.
    /// </summary>
    public class MuxCliService
    {
        #region Private-Members

        private readonly string _Header = "[MuxCliService] ";
        private readonly LoggingModule _Logging;
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
        public MuxCliService(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe a Mux captain configuration.
        /// </summary>
        public async Task<MuxProbeResult> ProbeAsync(Captain captain, CancellationToken token = default)
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

            return await ProbeAsync(captain.Model, options, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Probe a Mux endpoint selection directly.
        /// </summary>
        public async Task<MuxProbeResult> ProbeAsync(string? model, MuxCaptainOptions options, CancellationToken token = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            MuxCommandExecutionResult execution = await ExecuteAsync(
                MuxCommandBuilder.BuildProbeArguments(model, options),
                _DefaultTimeout,
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
            CancellationToken token)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "mux",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start mux.");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                throw new TimeoutException("mux command timed out after " + timeout.TotalSeconds.ToString("0") + " seconds.");
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _Logging.Debug(_Header + "mux exited with code " + process.ExitCode + ": " + FirstNonEmptyLine(stderr, stdout));
            }

            return new MuxCommandExecutionResult
            {
                ExitCode = process.ExitCode,
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
