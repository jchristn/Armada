namespace Armada.Runtimes.Usage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// OpenCode account usage. <c>opencode stats</c> reports session token and cost accounting, not remaining
    /// capacity, and its output format was not verified on the inspecting host (the CLI was absent), so no
    /// parser is enabled: the probe answers Unsupported. Session totals are never added to token records and
    /// never treated as a quota.
    /// </summary>
    public class OpenCodeUsageProbe : IUsageProbe
    {
        #region Public-Members

        /// <summary>
        /// Stable code returned for OpenCode.
        /// </summary>
        public const string UnavailableCode = "UnsupportedOpenCode";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public OpenCodeUsageProbe()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            UsageCaptureCapability capability = AgentRuntimeCapabilities.UsageCapture(Armada.Core.Enums.AgentRuntimeEnum.OpenCode);
            return Task.FromResult(UsageProbeResult.Unsupported(UnavailableCode, capability.Reason ?? "OpenCode has no remaining-quota source"));
        }

        #endregion
    }
}
