namespace Test.Shared.Infrastructure
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Test double for <see cref="ICaptainPromptRunner"/>: instead of launching an agent it records the call (captain,
    /// working directory, prompt, manifest contents, timeout) and runs a caller-supplied behavior, which typically
    /// writes fleet-recommendations.json into the working directory or waits for cancellation.
    /// </summary>
    public sealed class StubCaptainPromptRunner : ICaptainPromptRunner
    {
        #region Public-Members

        /// <summary>
        /// Behavior to run: receives the working directory, the prompt, the timeout, and the token, and returns the run
        /// outcome. Defaults to a successful exit that writes nothing.
        /// </summary>
        public Func<string, string, TimeSpan, CancellationToken, Task<CaptainPromptResult>> Behavior { get; set; } =
            (string dir, string prompt, TimeSpan timeout, CancellationToken token) => Task.FromResult(new CaptainPromptResult { ExitCode = 0 });

        /// <summary>
        /// Number of runs.
        /// </summary>
        public int Calls { get; private set; } = 0;

        /// <summary>
        /// Captain of the last run, or null.
        /// </summary>
        public Captain? LastCaptain { get; private set; } = null;

        /// <summary>
        /// Working directory of the last run, or null.
        /// </summary>
        public string? LastWorkingDirectory { get; private set; } = null;

        /// <summary>
        /// Prompt of the last run, or null.
        /// </summary>
        public string? LastPrompt { get; private set; } = null;

        /// <summary>
        /// REPOSITORIES.md contents seen by the last run, or null.
        /// </summary>
        public string? LastManifest { get; private set; } = null;

        /// <summary>
        /// Timeout passed to the last run.
        /// </summary>
        public TimeSpan LastTimeout { get; private set; } = TimeSpan.Zero;

        /// <summary>
        /// Optional hook invoked when a run starts (for example to read the captain's state).
        /// </summary>
        public Func<Captain, Task>? OnRunStarted { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CaptainPromptResult> RunAsync(
            Captain captain,
            string workingDirectory,
            string prompt,
            TimeSpan timeout,
            string? logFilePath,
            Func<int, Task>? onStarted,
            CancellationToken token = default)
        {
            Calls++;
            LastCaptain = captain;
            LastWorkingDirectory = workingDirectory;
            LastPrompt = prompt;
            LastTimeout = timeout;
            string manifest = Path.Combine(workingDirectory, "REPOSITORIES.md");
            LastManifest = File.Exists(manifest) ? await File.ReadAllTextAsync(manifest, CancellationToken.None).ConfigureAwait(false) : null;
            if (onStarted != null) await onStarted(99999).ConfigureAwait(false);
            if (OnRunStarted != null) await OnRunStarted(captain).ConfigureAwait(false);
            CaptainPromptResult result = await Behavior(workingDirectory, prompt, timeout, token).ConfigureAwait(false);
            result.ProcessId = 99999;
            return result;
        }

        #endregion
    }
}
