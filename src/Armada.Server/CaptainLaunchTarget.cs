namespace Armada.Server
{
    using System;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// A runtime ready to launch a captain, from <see cref="CaptainLaunchRouter.SelectAsync"/>: a remote runtime that runs
    /// the captain on a Harbor over its link, or a local runtime that runs it on the Admiral host.
    /// </summary>
    public class CaptainLaunchTarget
    {
        #region Public-Members

        /// <summary>
        /// The runtime to start.
        /// </summary>
        public IAgentRuntime Runtime { get; }

        /// <summary>
        /// The Harbor the captain runs on, or null when it runs on the Admiral host.
        /// </summary>
        public string? HarborId { get; }

        /// <summary>
        /// True when the captain runs on a Harbor.
        /// </summary>
        public bool IsRemote => HarborId != null;

        /// <summary>
        /// Why this target was chosen.
        /// </summary>
        public string Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runtime">The runtime to start.</param>
        /// <param name="harborId">The Harbor the captain runs on, or null for the Admiral host.</param>
        /// <param name="reason">Why this target was chosen.</param>
        public CaptainLaunchTarget(IAgentRuntime runtime, string? harborId, string reason)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            HarborId = String.IsNullOrWhiteSpace(harborId) ? null : harborId;
            Reason = reason ?? String.Empty;
        }

        #endregion
    }
}
