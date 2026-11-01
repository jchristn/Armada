namespace Armada.Core.Services
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Captures one account usage observation on the host that holds the credentials. Implemented by the
    /// host-side usage helper (the <c>Armada.Runtimes.UsageProbeHostRunner</c> and, later, the
    /// per-runtime probes) and invoked through the host-command seam: in Local mode the Admiral host runs
    /// it in-process, in Split mode the credential Harbor runs it on its own machine. The host context
    /// carries only a safe profile reference and probe configuration, never credential contents, and the
    /// returned result is already sanitized: allowlisted status and source, the host-derived account key,
    /// redacted diagnostics. A source the runtime cannot capture returns
    /// <see cref="Enums.UsageSnapshotStatusEnum.Unsupported"/> with a stable error code, never an error and
    /// never invented numbers.
    /// </summary>
    public interface IUsageProbe
    {
        /// <summary>
        /// Capture one usage observation for the account the host's active credential profile resolves to.
        /// </summary>
        /// <param name="host">Host context: tenant, stable host identity, runtime, mode and probe configuration.</param>
        /// <param name="token">Cancellation token; on cancellation the probe stops and its process tree is cleaned up.</param>
        /// <returns>The sanitized capture result. Never null: an unusable source returns Unsupported or Failed.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="host"/> is null.</exception>
        Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default);
    }
}