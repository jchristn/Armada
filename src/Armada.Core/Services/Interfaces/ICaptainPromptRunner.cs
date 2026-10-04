namespace Armada.Core.Services.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Runs a captain's agent process once on a prompt in a given working directory, outside any mission or dock, and
    /// waits for it to exit. Implementations launch on the Admiral host, enforce the timeout, and stop the process when
    /// the token is cancelled. Callers own captain reservation and read whatever files the agent was asked to write.
    /// </summary>
    public interface ICaptainPromptRunner
    {
        /// <summary>
        /// Run the captain on the prompt and wait for the process to exit, time out, or be cancelled.
        /// </summary>
        /// <param name="captain">Captain whose runtime, model, and options are used.</param>
        /// <param name="workingDirectory">Existing directory the agent runs in.</param>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="timeout">Maximum run time; the process is stopped when it is exceeded.</param>
        /// <param name="logFilePath">File the agent output is appended to, or null.</param>
        /// <param name="onStarted">Callback invoked with the process identifier once the process starts, or null.</param>
        /// <param name="token">Cancellation token; cancelling stops the process.</param>
        /// <returns>The run outcome. Never null; start failures are reported through <see cref="CaptainPromptResult.Error"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain, workingDirectory, or prompt is null.</exception>
        Task<CaptainPromptResult> RunAsync(
            Captain captain,
            string workingDirectory,
            string prompt,
            TimeSpan timeout,
            string? logFilePath,
            Func<int, Task>? onStarted,
            CancellationToken token = default);
    }
}
