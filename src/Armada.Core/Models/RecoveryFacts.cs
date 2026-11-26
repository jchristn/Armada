namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// What is known about a failure when <see cref="Armada.Core.Services.RecoveryPolicyEngine"/> decides how to recover
    /// (RS-5.10): where it happened, its kind, and the attempts already spent. Each site fills only the fields it uses.
    /// </summary>
    public class RecoveryFacts
    {
        /// <summary>Where the failure was seen.</summary>
        public RecoverySiteEnum Site { get; set; } = RecoverySiteEnum.RuntimeExit;

        /// <summary>False when the failure has no mission to recover; only Fail is possible then.</summary>
        public bool HasMission { get; set; } = true;

        /// <summary>True when the mission was cancelled; an interruption of a cancelled mission is not redispatched.</summary>
        public bool MissionCancelled { get; set; } = false;

        /// <summary>Runtime classification of the agent's exit, or null when none was made.</summary>
        public RuntimeFailureKindEnum? RuntimeFailureKind { get; set; } = null;

        /// <summary>Agent process exit code, or null. A negative code is an interruption, not an agent failure.</summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>Fresh re-dispatches already spent (interruptions, or no-op completions).</summary>
        public int Redispatches { get; set; } = 0;

        /// <summary>Most fresh re-dispatches allowed for this site.</summary>
        public int RedispatchLimit { get; set; } = 0;

        /// <summary>In-place resumes already spent by the captain on this mission.</summary>
        public int Resumes { get; set; } = 0;

        /// <summary>True when a transient outage is younger than the policy's grace period.</summary>
        public bool WithinOutageGrace { get; set; } = false;

        /// <summary>True when escalation on an open endpoint breaker is on and the captain's endpoint breaker is open.</summary>
        public bool EndpointBreakerOpen { get; set; } = false;

        /// <summary>True when the captain emitted a tool call the server could not parse twice in a row.</summary>
        public bool RepeatedMalformedToolCalls { get; set; } = false;

        /// <summary>True when the captain stopped at its iteration (model-turn) limit twice in a row.</summary>
        public bool RepeatedIterationLimit { get; set; } = false;

        /// <summary>Output-contract repairs already spent on this mission.</summary>
        public int OutputRepairs { get; set; } = 0;

        /// <summary>True when a person reviews this stage, so it is never rerun automatically.</summary>
        public bool ReviewGated { get; set; } = false;

        /// <summary>Judge rework rounds this Worker slice has had.</summary>
        public int ReworkRounds { get; set; } = 0;

        /// <summary>Escalation hops this Worker slice has had (in place and escalated rework rounds).</summary>
        public int Escalations { get; set; } = 0;

        /// <summary>Most escalation hops allowed (MaxEscalations).</summary>
        public int MaxEscalations { get; set; } = 0;

        /// <summary>True when the rework produced the same rejected commit as an earlier round.</summary>
        public bool ReworkSameCommit { get; set; } = false;

        /// <summary>The affected case the reviewer reported again after a rework round, or null.</summary>
        public string? ReworkRepeatedCase { get; set; } = null;

        /// <summary>True when the Worker's mode is read-only, so an unchanged commit is not stagnation.</summary>
        public bool WorkerReadOnly { get; set; } = false;

        /// <summary>True when an operator restarted the blocked stage, which spends a round outside the automatic budget.</summary>
        public bool OperatorRestart { get; set; } = false;

        /// <summary>Rescue missions already dispatched for the incident.</summary>
        public int Rescues { get; set; } = 0;

        /// <summary>Most rescues allowed (MaxMissionRecoveryAttempts).</summary>
        public int RescueBudget { get; set; } = 0;

        /// <summary>True when a rescue is still running.</summary>
        public bool RescueInFlight { get; set; } = false;

        /// <summary>True when the incident cannot link another rescue mission.</summary>
        public bool RescueLinksFull { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RecoveryFacts()
        {
        }

        /// <summary>
        /// Instantiate for a site.
        /// </summary>
        /// <param name="site">Where the failure was seen.</param>
        public RecoveryFacts(RecoverySiteEnum site)
        {
            Site = site;
        }
    }
}
