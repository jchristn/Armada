namespace Armada.Core.Services
{
    using Armada.Core.Harbor;
    using Armada.Core.Models;

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
        /// The job's latest activity arrived (only for a launch that asked for structured progress).
        /// </summary>
        /// <param name="activity">The activity.</param>
        void OnActivity(RuntimeActivity activity);

        /// <summary>
        /// The captain process exited.
        /// </summary>
        /// <param name="exitCode">Process exit code.</param>
        void OnExited(int exitCode);

        /// <summary>
        /// The Harbor reported that it could not carry out the job (an <c>error</c> message naming the job), for example
        /// because the runtime's CLI is not installed on the Harbor host. No further events follow for the job.
        /// </summary>
        /// <param name="message">The Harbor's error message.</param>
        void OnFailed(string message);
    }
}
