namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Harbor;

    /// <summary>
    /// Launches and manages captain processes on the Harbor host. Implemented by the Harbor app (which can
    /// reference the agent runtimes) and handed to the link client, so the client -- living in Armada.Core,
    /// which cannot depend on the runtime layer -- can carry out a delegated launch without knowing how a
    /// captain is actually spawned. Callbacks stream the process lifecycle back to the caller, which relays
    /// it to the Admiral over the link.
    /// </summary>
    public interface IHarborJobRunner
    {
        /// <summary>
        /// Start a captain process for a delegated launch. The callbacks fire as the process starts, emits
        /// output, and exits.
        /// </summary>
        /// <param name="request">The launch request from the Admiral.</param>
        /// <param name="mcpBaseUrl">MCP base URL the launched captain should use to reach the Admiral, or null.</param>
        /// <param name="onStarted">Invoked with the host process id once the process starts.</param>
        /// <param name="onOutput">Invoked for each chunk of output (stdout or stderr).</param>
        /// <param name="onExited">Invoked with the exit code when the process exits.</param>
        /// <param name="token">Cancellation token.</param>
        Task StartAsync(
            HarborLaunchRequest request,
            string? mcpBaseUrl,
            Action<int> onStarted,
            Action<HarborOutputStreamEnum, string> onOutput,
            Action<int> onExited,
            CancellationToken token);

        /// <summary>
        /// Stop a running job, allowing a graceful window before a forced kill of the process tree.
        /// </summary>
        /// <param name="jobId">Job identifier to stop.</param>
        /// <param name="gracefulTimeoutMs">Milliseconds to wait before a forced kill.</param>
        /// <param name="token">Cancellation token.</param>
        Task StopAsync(string jobId, int gracefulTimeoutMs, CancellationToken token);
    }
}
