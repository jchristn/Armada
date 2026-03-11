namespace Armada.Runtimes.Interfaces
{
    /// <summary>
    /// Interface for agent runtime adapters.
    /// </summary>
    public interface IAgentRuntime
    {
        /// <summary>
        /// Runtime display name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this runtime supports session resume.
        /// </summary>
        bool SupportsResume { get; }

        /// <summary>
        /// Event raised when the agent writes a line to stdout.
        /// The int parameter is the process ID, the string is the output line.
        /// </summary>
        event Action<int, string>? OnOutputReceived;

        /// <summary>
        /// Start an agent process with the given prompt in the specified working directory.
        /// Returns the process ID.
        /// </summary>
        /// <param name="workingDirectory">Working directory for the agent.</param>
        /// <param name="prompt">Initial prompt or mission description.</param>
        /// <param name="environment">Additional environment variables.</param>
        /// <param name="logFilePath">Optional path to write agent output log.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Process ID of the started agent.</returns>
        Task<int> StartAsync(
            string workingDirectory,
            string prompt,
            Dictionary<string, string>? environment = null,
            string? logFilePath = null,
            CancellationToken token = default);

        /// <summary>
        /// Stop an agent process gracefully.
        /// </summary>
        /// <param name="processId">Process ID to stop.</param>
        /// <param name="token">Cancellation token.</param>
        Task StopAsync(int processId, CancellationToken token = default);

        /// <summary>
        /// Check if an agent process is still running.
        /// </summary>
        /// <param name="processId">Process ID to check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the process is running.</returns>
        Task<bool> IsRunningAsync(int processId, CancellationToken token = default);
    }
}
