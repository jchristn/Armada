namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Where a Command fleet action target runs: the executor, whether it is a Harbor, and the Harbor's reported
    /// OS (which decides the shell).
    /// </summary>
    internal sealed class FleetActionExecutorSelection
    {
        #region Public-Members

        /// <summary>
        /// Executor that runs the target's commands.
        /// </summary>
        public IHostCommandExecutor Executor { get; }

        /// <summary>
        /// Whether the executor runs on a Harbor rather than the Admiral host.
        /// </summary>
        public bool IsRemote { get; }

        /// <summary>
        /// The Harbor's reported OS description, or null.
        /// </summary>
        public string? HarborOsPlatform { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="executor">Executor.</param>
        /// <param name="isRemote">Whether the executor is a Harbor.</param>
        /// <param name="harborOsPlatform">Harbor OS description, or null.</param>
        public FleetActionExecutorSelection(IHostCommandExecutor executor, bool isRemote, string? harborOsPlatform)
        {
            Executor = executor ?? throw new ArgumentNullException(nameof(executor));
            IsRemote = isRemote;
            HarborOsPlatform = harborOsPlatform;
        }

        #endregion
    }
}
