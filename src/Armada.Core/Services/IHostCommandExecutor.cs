namespace Armada.Core.Services
{
    /// <summary>
    /// Runs host commands (git, gh) either in-process on the Admiral (Local mode) or delegated to a Harbor
    /// over its link (Split mode). The seam lets callers such as git and dock services run host commands
    /// without caring where the filesystem and tools physically live.
    /// </summary>
    public interface IHostCommandExecutor
    {
        /// <summary>
        /// Run a host command and return its result.
        /// </summary>
        /// <param name="request">Command to run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The command result.</returns>
        Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default);
    }
}
