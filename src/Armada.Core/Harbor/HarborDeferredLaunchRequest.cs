namespace Armada.Core.Harbor
{
    /// <summary>
    /// A one-shot, fire-before-death instruction from the Admiral to a Harbor: after the Admiral exits, launch
    /// the new slot's server, poll its health endpoint, and if it does not come up healthy within the timeout,
    /// roll the slot pointer back and launch the previous slot. Delivered and acknowledged while the Admiral is
    /// still alive, so the link being dead during the actual cutover is irrelevant. See docs/SERVER_REBUILD.md
    /// and docs/HARBOR_PROTOCOL.md.
    /// </summary>
    public class HarborDeferredLaunchRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Correlation id for the acknowledgement. Required.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the new slot's server executable to launch.
        /// </summary>
        public string LaunchExePath { get; set; } = string.Empty;

        /// <summary>
        /// Process id of the exiting Admiral; passed to the launched process via ARMADA_RESTART_WAIT_PID so it
        /// waits for that process to exit (freeing the port) before binding.
        /// </summary>
        public int WaitForPid { get; set; } = 0;

        /// <summary>
        /// Working directory for the launched process. Empty uses the executable's directory.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Health URL to poll after launch (for example http://127.0.0.1:7890/api/v1/status/health).
        /// </summary>
        public string HealthUrl { get; set; } = string.Empty;

        /// <summary>
        /// Seconds to wait for the new slot to report healthy before rolling back.
        /// </summary>
        public int HealthTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Absolute path to the previous slot's server executable, launched on rollback.
        /// </summary>
        public string FallbackExePath { get; set; } = string.Empty;

        /// <summary>
        /// Previous slot name, written back into the pointer file on rollback.
        /// </summary>
        public string FallbackSlot { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path to the slot pointer file to rewrite on rollback.
        /// </summary>
        public string CurrentPointerPath { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborDeferredLaunchRequest()
        {
        }

        #endregion
    }
}
