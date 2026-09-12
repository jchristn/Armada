namespace Armada.Core.Services
{
    using Armada.Core.Harbor;

    /// <summary>
    /// Receives the lifecycle of a delegated captain job as the Harbor reports it back over the link. The
    /// server-side remote runtime implements this and registers with the connection manager for its job id,
    /// translating the events into the ordinary agent-runtime events the rest of the Admiral expects.
    /// </summary>
    public interface IHarborJobListener
    {
        /// <summary>
        /// The captain process started on the Harbor.
        /// </summary>
        /// <param name="processId">Host process id on the Harbor.</param>
        void OnStarted(int processId);

        /// <summary>
        /// A chunk of captain output arrived.
        /// </summary>
        /// <param name="stream">Which standard stream the chunk came from.</param>
        /// <param name="data">The output chunk.</param>
        void OnOutput(HarborOutputStreamEnum stream, string data);

        /// <summary>
        /// The captain process exited.
        /// </summary>
        /// <param name="exitCode">Process exit code.</param>
        void OnExited(int exitCode);
    }
}
