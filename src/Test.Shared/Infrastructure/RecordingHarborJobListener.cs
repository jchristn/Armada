namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Records the lifecycle callbacks a Harbor job receives.
    /// </summary>
    public sealed class RecordingHarborJobListener : IHarborJobListener
    {
        #region Public-Members

        /// <summary>
        /// Process id reported by OnStarted, or null.
        /// </summary>
        public int? ProcessId { get; private set; } = null;

        /// <summary>
        /// Output chunks received.
        /// </summary>
        public List<string> Output { get; } = new List<string>();

        /// <summary>
        /// Activities received.
        /// </summary>
        public List<RuntimeActivity> Activities { get; } = new List<RuntimeActivity>();

        /// <summary>
        /// Exit code reported by OnExited, or null.
        /// </summary>
        public int? ExitCode { get; private set; } = null;

        /// <summary>
        /// Failure message reported by OnFailed, or null.
        /// </summary>
        public string? Failure { get; private set; } = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void OnStarted(int processId)
        {
            ProcessId = processId;
        }

        /// <inheritdoc />
        public void OnOutput(HarborOutputStreamEnum stream, string data)
        {
            Output.Add(data);
        }

        /// <inheritdoc />
        public void OnActivity(RuntimeActivity activity)
        {
            Activities.Add(activity);
        }

        /// <inheritdoc />
        public void OnExited(int exitCode)
        {
            ExitCode = exitCode;
        }

        /// <inheritdoc />
        public void OnFailed(string message)
        {
            Failure = message;
        }

        #endregion
    }
}
