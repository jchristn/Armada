namespace Armada.Runtimes.Usage
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Claude Code account usage. The only verified Claude source is passive: the
    /// <c>rate_limit_event</c> lines a real mission's <c>stream-json</c> output already carries, decoded by
    /// <see cref="ClaudeMissionStreamDecoder"/> through <see cref="ClaudeRateLimitUsage"/>. The candidate OAuth
    /// usage endpoint is undocumented and phase 0 could not prove that the CLI's credential may read it
    /// (PROBE_INTERFACES.md), so this active probe stays disabled: it answers Unsupported, never reads the
    /// credential store, never makes a network request and never sends a prompt. The interactive
    /// <c>/usage</c> PTY scrape is a separate explicit opt-in with no verified adapter in this build; it is
    /// never an automatic fallback and <c>/usage</c> is never sent as a model prompt.
    /// </summary>
    public class ClaudeUsageProbe : IUsageProbe
    {
        #region Public-Members

        /// <summary>
        /// Stable code returned while the active endpoint is unverified.
        /// </summary>
        public const string UnverifiedCode = "ClaudeUsageEndpointUnverified";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ClaudeUsageProbe()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            string reason = "the Claude Code OAuth usage endpoint is undocumented and has not been verified for the CLI credential, so no active read is made; "
                + "passive rate_limit_event observations from running missions are harvested instead";
            if (host.AllowPty)
                reason += "; the separately opted-in PTY /usage source has no verified adapter in this build and is never sent as a prompt";
            return Task.FromResult(UsageProbeResult.Unsupported(UnverifiedCode, reason));
        }

        #endregion
    }
}
