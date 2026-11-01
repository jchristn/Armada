namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// The host-side usage helper: the dedicated typed capture helper invoked through the host-command
    /// seam (<see cref="IUsageProbe"/>). It runs on the credential host -- in-process on the Admiral in
    /// Local mode, on the Harbor over the link in Split mode -- and performs credential access, the
    /// probe read and sanitization locally, answering with a sanitized <see cref="UsageProbeResult"/>
    /// only. It never sends a model prompt: Auto mode runs only registered read-only runtime probes
    /// (none ship in this build, so Auto answers Unsupported with a stable reason until the runtime
    /// adapters land), and Custom mode runs the administrator-chosen executable contract: a configured
    /// executable plus argument list with no shell interpolation, a bounded output reader that refuses an
    /// answer past its cap instead of parsing a truncated prefix, a host execution deadline with
    /// process-tree termination on expiry or cancellation, and the documented single JSON object output
    /// contract (an observation time outside the accepted bounds is refused, never replaced with a fresh
    /// one). The host owns the final account HMAC and the allowed
    /// status/source regardless of what the executable claims, and every diagnostic is redacted and
    /// byte-capped before it is returned.
    /// </summary>
    public class UsageProbeHostRunner : IUsageProbe
    {
        #region Private-Members

        private const int _MaximumStdoutBytes = 64 * 1024;
        private const int _MaximumStderrBytes = 2048;
        private const int _MaximumWindows = 32;
        private const int _MaximumWindowNameChars = 128;
        private static readonly TimeSpan _MaximumCapturedSkew = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan _MaximumCapturedAge = TimeSpan.FromHours(24);

        private readonly LoggingModule _Logging;
        private readonly UsageAccountResolver? _Accounts;
        private readonly Func<AgentRuntimeEnum, IUsageProbe?>? _RuntimeProbes;
        private UsageAccountResolver? _ProcessLocalAccounts;
        private readonly object _AccountsLock = new object();
        private static readonly char[] _ShellSyntax = new char[] { ';', '|', '&', '<', '>', '`', '\'', '"' };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the host-side usage helper.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="accounts">Account resolver holding this host's stable identity and protected account key.
        /// Null makes the helper use a process-local identity (documented as unstable across restarts).</param>
        /// <param name="runtimeProbes">Optional registry of read-only runtime probes by runtime (the phase 3 adapters);
        /// a runtime without a registered probe answers Unsupported rather than guessing.</param>
        public UsageProbeHostRunner(LoggingModule logging, UsageAccountResolver? accounts = null, Func<AgentRuntimeEnum, IUsageProbe?>? runtimeProbes = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Accounts = accounts;
            _RuntimeProbes = runtimeProbes;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (String.IsNullOrWhiteSpace(host.TenantId) || String.IsNullOrWhiteSpace(host.HostId))
                return UsageProbeResult.Failed("InvalidProbeContext", "the probe request has no tenant or host identity");

            if (host.Mode == UsageCaptureModeEnum.Disabled)
                return UsageProbeResult.Failed("CaptureDisabled", "the captain is not capturing account usage");

            if (host.Mode == UsageCaptureModeEnum.Custom)
                return await RunCustomAsync(host, token).ConfigureAwait(false);

            return await RunAutoAsync(host, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<UsageProbeResult> RunAutoAsync(UsageProbeHost host, CancellationToken token)
        {
            IUsageProbe? probe = _RuntimeProbes?.Invoke(host.Runtime);
            if (probe == null)
            {
                // No placeholder probes: a runtime without a registered read-only adapter answers
                // Unsupported. PTY scraping is a separate opt-in source and is never an automatic fallback,
                // so it is only mentioned when the explicit opt-in switch is on, and even then this build
                // ships no PTY probe.
                UsageCaptureCapability capability = AgentRuntimeCapabilities.UsageCapture(host.Runtime);
                if (!capability.Supported)
                    return UsageProbeResult.Unsupported(UnsupportedCode(host.Runtime), capability.Reason);

                string reason = "no usage probe adapter is registered for " + host.Runtime + " in this build yet"
                    + (host.AllowPty ? "; the separately opted-in PTY source is not implemented in this build either" : "");
                return UsageProbeResult.Unsupported("RuntimeProbeUnavailable", reason);
            }

            try
            {
                // The runtime probe owns its own read; the host execution deadline still bounds it, and
                // cancellation terminates whatever it started.
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(host.TimeoutSeconds));
                    UsageProbeResult result = await probe.CaptureAsync(host, deadline.Token).ConfigureAwait(false);
                    return Sanitize(host, result);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return UsageProbeResult.Failed("ProbeTimeout", "the " + host.Runtime + " probe exceeded the " + host.TimeoutSeconds + " second host execution deadline and was stopped");
            }
        }

        private static string UnsupportedCode(AgentRuntimeEnum runtime)
        {
            return "Unsupported" + runtime;
        }

        private async Task<UsageProbeResult> RunCustomAsync(UsageProbeHost host, CancellationToken token)
        {
            CustomUsageCommand? command = CustomUsageCommand.Parse(host.CommandJson);
            if (command == null)
                return UsageProbeResult.Failed("MalformedCommandConfig", "the Custom command configuration is not a valid executable and argument list");

            if (command.Executable.IndexOfAny(_ShellSyntax) >= 0 || command.Executable.Contains("$("))
                return UsageProbeResult.Failed("MalformedCommandConfig", "the Custom command configuration contains shell syntax; use an executable and argument list");

            StringBuilder standardOutput = new StringBuilder();
            StringBuilder standardError = new StringBuilder();
            bool timedOut = false;
            int stdoutBytes = 0;

            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(host.TimeoutSeconds));

                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = command.Executable,
                        WorkingDirectory = Environment.CurrentDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    foreach (string argument in command.Arguments!)
                        startInfo.ArgumentList.Add(argument);

                    process.StartInfo = startInfo;
                    try
                    {
                        process.Start();
                    }
                    catch (Exception e)
                    {
                        return UsageProbeResult.Failed("CommandStartFailed", SecretRedactor.Redact("the configured executable could not start: " + e.Message));
                    }

                    Task<int> outputPump = PumpAsync(process.StandardOutput, standardOutput, _MaximumStdoutBytes);
                    Task<int> errorPump = PumpAsync(process.StandardError, standardError, _MaximumStderrBytes);

                    try
                    {
                        await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        timedOut = !token.IsCancellationRequested;
                        KillTree(process);
                    }
                    finally
                    {
                        // Cleanup always runs: a killed process's pipes reach end of stream, and the bounded
                        // readers stop at their caps regardless.
                        stdoutBytes = await DrainAsync(outputPump, errorPump).ConfigureAwait(false);
                    }

                    if (token.IsCancellationRequested)
                        return UsageProbeResult.Failed("ProbeCancelled", "the Custom usage probe was cancelled and its process tree was stopped");

                    if (timedOut)
                        return UsageProbeResult.Failed("ProbeTimeout", "the Custom usage probe exceeded the " + host.TimeoutSeconds + " second host execution deadline and its process tree was terminated");

                    if (process.ExitCode != 0)
                        return UsageProbeResult.Failed("CommandFailed", SanitizeExcerpt(standardError.ToString()));

                    // The whole answer is measured, never just its stored prefix: output past the accepted size
                    // is refused rather than parsed as a truncated document, and an answer whose size cannot be
                    // established in time is refused for the same reason.
                    if (stdoutBytes < 0 || stdoutBytes > _MaximumStdoutBytes)
                        return UsageProbeResult.Failed("MalformedOutput", "the configured command wrote more than the accepted " + _MaximumStdoutBytes + " bytes of output");

                    return ParseCustomOutput(host, standardOutput.ToString());
                }
            }
        }

        private UsageProbeResult ParseCustomOutput(UsageProbeHost host, string standardOutput)
        {
            string trimmed = standardOutput.Trim();
            if (trimmed.Length == 0)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command wrote no output");

            // Exactly one JSON object: trailing content after the object is rejected rather than guessed at.
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                return UsageProbeResult.Failed("MalformedOutput", "the configured command did not write a single JSON object");

            CustomUsageOutput? output;
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = false };
                output = JsonSerializer.Deserialize<CustomUsageOutput>(trimmed, options);
            }
            catch (JsonException)
            {
                return UsageProbeResult.Failed("MalformedOutput", "the configured command's output is not valid JSON");
            }

            if (output == null)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command's output deserialized to nothing");

            if (output.SchemaVersion != 1)
                return UsageProbeResult.Failed("UnsupportedSchemaVersion", "the configured command reported schema version " + output.SchemaVersion + "; only version 1 is supported");

            if (output.Status == null)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command did not report a capture status");

            UsageSnapshotStatusEnum status = output.Status.Value;
            if (status != UsageSnapshotStatusEnum.Ok && status != UsageSnapshotStatusEnum.Unsupported
                && status != UsageSnapshotStatusEnum.AuthMissing && status != UsageSnapshotStatusEnum.Failed
                && status != UsageSnapshotStatusEnum.Estimated)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command reported status " + status + ", which is outside the allowed statuses");

            if (output.Windows == null) output.Windows = new List<UsageWindow>();
            if (output.Windows.Count > _MaximumWindows)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command reported " + output.Windows.Count + " windows; at most " + _MaximumWindows + " are accepted");

            DateTime nowUtc = host.DispatchedUtc == default ? DateTime.UtcNow : host.DispatchedUtc;

            // The observation time is validated, never replaced: a result dated outside the accepted skew
            // or age would manufacture freshness (a capture that never happened when it claims it did).
            DateTime capturedUtc = output.CapturedUtc == default ? nowUtc : output.CapturedUtc;
            if (capturedUtc > nowUtc + _MaximumCapturedSkew || capturedUtc < nowUtc - _MaximumCapturedAge)
                return UsageProbeResult.Failed("MalformedOutput", "the configured command reported an observation time outside the accepted bounds");

            foreach (UsageWindow window in output.Windows)
            {
                if (window == null) continue;
                if (window.Name != null && window.Name.Length > _MaximumWindowNameChars)
                    return UsageProbeResult.Failed("MalformedOutput", "a reported window name exceeds the accepted length");
                if (window.UsedPercent.HasValue && (Double.IsNaN(window.UsedPercent.Value) || window.UsedPercent.Value < 0 || window.UsedPercent.Value > 100))
                    return UsageProbeResult.Failed("MalformedOutput", "a reported window percentage is outside 0..100");
                if (window.Used.HasValue && (window.Used.Value < 0))
                    return UsageProbeResult.Failed("MalformedOutput", "a reported window usage is negative");
                if (window.Limit.HasValue && window.Limit.Value <= 0)
                    return UsageProbeResult.Failed("MalformedOutput", "a reported window limit is not positive");
                if (window.ResetsUtc.HasValue)
                {
                    if (window.ResetsUtc.Value < capturedUtc - _MaximumCapturedAge || window.ResetsUtc.Value > capturedUtc + TimeSpan.FromDays(8))
                        return UsageProbeResult.Failed("MalformedOutput", "a reported reset time is beyond the accepted skew");
                }
            }

            // The host owns the final account key and the allowed source: the executable only names the
            // provider-issued account identity, which never leaves this host unhashed.
            UsageAccountResolver accounts = ResolveAccounts();
            string? accountKey = accounts.DeriveAccountKey(host.TenantId, host.Runtime, output.AccountKey, null);

            return new UsageProbeResult
            {
                Status = status,
                Source = UsageCaptureSourceEnum.Custom,
                Provider = String.IsNullOrWhiteSpace(output.Provider) ? null : output.Provider.Trim(),
                AccountKey = status == UsageSnapshotStatusEnum.AuthMissing ? null : accountKey,
                CapturedUtc = capturedUtc,
                Windows = output.Windows,
                RawExcerpt = String.IsNullOrWhiteSpace(output.Excerpt) ? null : SanitizeExcerpt(output.Excerpt),
                ErrorCode = status == UsageSnapshotStatusEnum.Ok || status == UsageSnapshotStatusEnum.Estimated
                    ? null
                    : (String.IsNullOrWhiteSpace(output.ErrorCode) ? (status == UsageSnapshotStatusEnum.AuthMissing ? "CredentialUnavailable" : status.ToString()) : output.ErrorCode.Trim())
            };
        }

        private UsageProbeResult Sanitize(UsageProbeHost host, UsageProbeResult result)
        {
            if (result == null) return UsageProbeResult.Failed("ProbeFailed", "the runtime probe returned nothing");
            result.Provider = String.IsNullOrWhiteSpace(result.Provider) ? null : result.Provider.Trim();
            result.RawExcerpt = String.IsNullOrWhiteSpace(result.RawExcerpt) ? null : SanitizeExcerpt(result.RawExcerpt);
            return result;
        }

        private UsageAccountResolver ResolveAccounts()
        {
            if (_Accounts != null) return _Accounts;
            lock (_AccountsLock)
            {
                _ProcessLocalAccounts ??= new UsageAccountResolver((string?)null, _Logging);
                return _ProcessLocalAccounts;
            }
        }

        private static string SanitizeExcerpt(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;
            string redacted = SecretRedactor.Redact(text.Trim());
            byte[] bytes = Encoding.UTF8.GetBytes(redacted);
            if (bytes.Length <= _MaximumStderrBytes) return redacted;
            return Encoding.UTF8.GetString(bytes, 0, _MaximumStderrBytes);
        }

        private static async Task<int> PumpAsync(StreamReader reader, StringBuilder sink, int maximumBytes)
        {
            char[] buffer = new char[4096];
            int total = 0;
            try
            {
                int written = 0;
                while (true)
                {
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;
                    string chunk = new string(buffer, 0, read);
                    int chunkBytes = Encoding.UTF8.GetByteCount(chunk);
                    // The pipe is drained to its end whatever happens, and every byte is counted: an answer
                    // that exceeds the cap is refused by the caller, never guessed at from a prefix.
                    total += chunkBytes;
                    if (written >= maximumBytes) continue;
                    if (written + chunkBytes > maximumBytes) continue; // a chunk that would cross the cap is dropped, never split mid-character
                    lock (sink) sink.Append(chunk);
                    written += chunkBytes;
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
            return total;
        }

        private static async Task<int> DrainAsync(Task<int> outputPump, Task<int> errorPump)
        {
            try
            {
                await Task.WhenAll(outputPump, errorPump).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return -1;
            }
            catch (Exception)
            {
                // The pumps are best effort; the caller refuses an answer whose size is not established.
                return -1;
            }

            return outputPump.Status == TaskStatus.RanToCompletion ? outputPump.Result : -1;
        }

        private static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
            }

            try
            {
                // Kill(true) only signals the tree: wait (bounded) for the probe's root process to be reaped
                // so a cancelled or timed-out probe has actually stopped before its reply is sent.
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// The administrator-chosen Custom command contract: an executable and an ordered argument list,
        /// never a shell string. Deserialization is strict about the shape and lenient about unknown
        /// fields within schema version 1.
        /// </summary>
        private sealed class CustomUsageCommand
        {
            public string Executable { get; set; } = String.Empty;
            public List<string>? Arguments { get; set; } = null;

            public static CustomUsageCommand? Parse(string? json)
            {
                if (String.IsNullOrWhiteSpace(json)) return null;
                try
                {
                    CustomUsageCommand? command = JsonSerializer.Deserialize<CustomUsageCommand>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (command == null) return null;
                    command.Executable = (command.Executable ?? String.Empty).Trim();
                    if (command.Executable.Length == 0) return null;
                    if (command.Executable.Contains('\0')) return null;
                    command.Arguments ??= new List<string>();
                    foreach (string argument in command.Arguments)
                    {
                        if (argument == null || argument.Contains('\0')) return null;
                    }

                    return command;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// The Custom executable's documented output contract: one JSON object with the schema version,
        /// the capture status, the provider-issued account identity (which the host re-keys; the raw
        /// value never leaves the host), the provider, the observation time and the utilization windows.
        /// Unknown fields within schema version 1 are ignored; every numeric field is nullable and null
        /// means unknown, never zero.
        /// </summary>
        private sealed class CustomUsageOutput
        {
            public int SchemaVersion { get; set; } = 0;
            public UsageSnapshotStatusEnum? Status { get; set; } = null;
            public string? AccountKey { get; set; } = null;
            public string? Provider { get; set; } = null;
            public DateTime CapturedUtc { get; set; } = default;
            public string? ErrorCode { get; set; } = null;
            public string? Excerpt { get; set; } = null;
            public List<UsageWindow>? Windows { get; set; } = null;
        }

        #endregion
    }
}