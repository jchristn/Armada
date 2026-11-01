namespace Armada.Runtimes.Usage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Gemini account usage. No read-only remaining-quota interface is verified (PROBE_INTERFACES.md):
    /// <c>/stats</c> is session accounting and a <c>RESOURCE_EXHAUSTED</c> error carries no reliable reset.
    /// A Derived estimate needs an operator-configured budget, unit, reset timezone and source revision,
    /// none of which this build configures, so the probe answers Unsupported. It never invents a midnight
    /// reset or an authoritative 100 percent, and never sends a prompt.
    /// </summary>
    public class GeminiUsageProbe : IUsageProbe
    {
        #region Public-Members

        /// <summary>
        /// Stable code returned while no budget is configured.
        /// </summary>
        public const string UnavailableCode = "GeminiQuotaUnavailable";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GeminiUsageProbe()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return Task.FromResult(UsageProbeResult.Unsupported(UnavailableCode,
                "Gemini has no verified read-only quota interface; a Derived estimate requires a configured budget, unit and reset timezone, and none is configured"));
        }

        #endregion
    }
}
