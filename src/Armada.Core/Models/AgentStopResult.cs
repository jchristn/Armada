namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Whether a stop reached the recorded process, found nothing running, or left a process running.
    /// </summary>
    public sealed class AgentStopResult
    {
        /// <summary>
        /// The recorded process was stopped.
        /// </summary>
        public const string StoppedReason = "stopped";

        /// <summary>
        /// No process with that id was running.
        /// </summary>
        public const string NotRunningReason = "not_running";

        /// <summary>
        /// The process start time was missing or could not be read.
        /// </summary>
        public const string IdentityUnverifiedReason = "process_identity_unverified";

        /// <summary>
        /// The process id belongs to a different start time.
        /// </summary>
        public const string IdentifierReusedReason = "process_identifier_reused";

        /// <summary>
        /// The recorded process was still running after the stop attempt.
        /// </summary>
        public const string KillFailedReason = "kill_failed";

        private AgentStopResult(AgentStopOutcomeEnum outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        /// <summary>
        /// Stop outcome.
        /// </summary>
        public AgentStopOutcomeEnum Outcome { get; }

        /// <summary>
        /// Stable reason name.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// True when the recorded process was stopped or was already gone.
        /// </summary>
        public bool Cleared
        {
            get
            {
                return Outcome != AgentStopOutcomeEnum.Refused;
            }
        }

        /// <summary>
        /// The recorded process was stopped.
        /// </summary>
        /// <returns>A stopped result.</returns>
        public static AgentStopResult Stopped()
        {
            return new AgentStopResult(AgentStopOutcomeEnum.Stopped, StoppedReason);
        }

        /// <summary>
        /// No process with that id was running.
        /// </summary>
        /// <returns>A not-running result.</returns>
        public static AgentStopResult NotRunning()
        {
            return new AgentStopResult(AgentStopOutcomeEnum.NotRunning, NotRunningReason);
        }

        /// <summary>
        /// The process was left running.
        /// </summary>
        /// <param name="reason">Stable reason name.</param>
        /// <returns>A refused result.</returns>
        public static AgentStopResult Refused(string reason)
        {
            return new AgentStopResult(AgentStopOutcomeEnum.Refused, reason ?? IdentityUnverifiedReason);
        }
    }
}
