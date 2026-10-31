namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;

    /// <summary>
    /// Runs host commands on a specific Harbor by delegating them over its link. This is the Split-mode
    /// implementation of the host-command seam; each instance targets one Harbor (resolved by dock affinity
    /// or routing), so the identical <see cref="HostCommandRequest"/> a caller would run in-process is
    /// instead carried out on the Harbor host.
    /// </summary>
    public class RemoteHostCommandExecutor : IHostCommandExecutor
    {
        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;
        private const int _ReplyGraceMs = 15000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific target Harbor.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Target Harbor identifier.</param>
        public RemoteHostCommandExecutor(HarborConnectionManager manager, string harborId)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (request.Kind == HostCommandKindEnum.UsageProbe)
                return await RunUsageProbeAsync(request, token).ConfigureAwait(false);

            HarborGitRequest gitRequest = new HarborGitRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Executable = request.Executable,
                WorkingDirectory = request.WorkingDirectory,
                Arguments = request.Arguments,
                TimeoutMs = request.TimeoutMs,
                ExpectedExitCodes = request.ExpectedExitCodes.Count > 0 ? new List<int>(request.ExpectedExitCodes) : null,
                Kind = request.Kind == HostCommandKindEnum.Routine ? null : request.Kind,
                Label = String.IsNullOrWhiteSpace(request.Label) ? null : request.Label
            };

            // The Harbor stops the command at TimeoutMs; wait somewhat longer (up to 15 seconds more) so its result, rather
            // than a local timeout, reports what happened.
            int waitMs = request.TimeoutMs > 0 ? request.TimeoutMs + Math.Min(request.TimeoutMs, _ReplyGraceMs) : 0;
            HarborGitResult? result = await _Manager.SendGitAsync(_HarborId, gitRequest, waitMs, token).ConfigureAwait(false);
            if (result == null)
            {
                return new HostCommandResult
                {
                    ExitCode = -1,
                    TimedOut = true,
                    StandardError = "Harbor " + _HarborId + " did not respond within the timeout."
                };
            }

            return new HostCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                TimedOut = result.TimedOut
            };
        }

        #endregion

        #region Private-Methods

        private async Task<HostCommandResult> RunUsageProbeAsync(HostCommandRequest request, CancellationToken token)
        {
            // Allowlist: a usage probe is sent only to a Harbor that advertised the usage-probe capability.
            // An older Harbor answers Unsupported (update it), a disconnected one Failed with its last good
            // data retained; neither falls back to this host's credentials.
            if (!_Manager.IsConnected(_HarborId))
            {
                return ProbeReply(UsageProbeResult.Failed("HostUnavailable", "Harbor " + _HarborId + " is not connected; the last good observation is kept"));
            }

            if (!_Manager.HostsUsageProbes(_HarborId))
            {
                return ProbeReply(UsageProbeResult.Unsupported("HostUpgradeRequired", "Harbor " + _HarborId + " cannot run usage probes; update it"));
            }

            HarborGitRequest probeRequest = new HarborGitRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                TimeoutMs = request.TimeoutMs,
                Kind = HostCommandKindEnum.UsageProbe,
                Label = String.IsNullOrWhiteSpace(request.Label) ? null : request.Label,
                UsageProbeJson = request.UsageProbeJson
            };

            // The Harbor stops the probe at TimeoutMs (the execution deadline); the Admiral waits for the bounded
            // round-trip deadline only, which stays execution duration plus the reply grace, never transit time.
            int waitMs = request.TimeoutMs > 0 ? request.TimeoutMs + Math.Min(request.TimeoutMs, _ReplyGraceMs) : 0;
            HarborGitResult? result = await _Manager.SendGitAsync(_HarborId, probeRequest, waitMs, token).ConfigureAwait(false);
            if (result == null)
            {
                return ProbeReply(UsageProbeResult.Failed("ProbeTimeout", "Harbor " + _HarborId + " did not answer the usage probe within the round-trip deadline"));
            }

            if (String.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return ProbeReply(UsageProbeResult.Failed("ProbeTimeout", result.TimedOut
                    ? "Harbor " + _HarborId + " stopped the usage probe at its execution deadline"
                    : "Harbor " + _HarborId + " answered the usage probe without a result payload"));
            }

            return new HostCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                TimedOut = result.TimedOut
            };
        }

        private static HostCommandResult ProbeReply(UsageProbeResult result)
        {
            return new HostCommandResult
            {
                ExitCode = 0,
                StandardOutput = result.Serialize(),
                StandardError = String.IsNullOrWhiteSpace(result.ErrorCode) ? String.Empty : result.ErrorCode,
                TimedOut = String.Equals(result.ErrorCode, "ProbeTimeout", StringComparison.Ordinal)
            };
        }

        #endregion
    }
}
