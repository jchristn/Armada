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
    /// (<see cref="Usage.UsageProbeRegistry"/> in production; without a registry Auto answers Unsupported
    /// with a stable reason), and Custom mode runs the administrator-chosen executable contract
    /// (<see cref="Usage.CustomUsageProbe"/>): a configured
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

        private const int _MaximumStderrBytes = 2048;

        private readonly LoggingModule _Logging;
        private readonly UsageAccountResolver? _Accounts;
        private readonly Func<AgentRuntimeEnum, IUsageProbe?>? _RuntimeProbes;
        private UsageAccountResolver? _ProcessLocalAccounts;
        private readonly object _AccountsLock = new object();

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

        private Task<UsageProbeResult> RunCustomAsync(UsageProbeHost host, CancellationToken token)
        {
            // The Custom contract lives in its own probe; the host still owns the account resolver it HMACs with.
            return new Usage.CustomUsageProbe(_Logging, ResolveAccounts()).CaptureAsync(host, token);
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

        #endregion
    }
}
