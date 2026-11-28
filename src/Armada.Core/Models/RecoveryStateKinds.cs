namespace Armada.Core.Models
{
    /// <summary>
    /// Kinds of <see cref="RecoveryStateEntry"/>.
    /// </summary>
    public static class RecoveryStateKinds
    {
        /// <summary>Circuit breaker of a model endpoint, keyed by the endpoint's capacity key.</summary>
        public const string EndpointBreaker = "EndpointBreaker";

        /// <summary>Launch backoff of a mission, keyed by mission id.</summary>
        public const string LaunchFailure = "LaunchFailure";

        /// <summary>Start of a transient model outage for a mission, keyed by mission id.</summary>
        public const string TransientOutage = "TransientOutage";

        /// <summary>Last time an escalation rule fired, keyed by trigger and entity.</summary>
        public const string EscalationCooldown = "EscalationCooldown";
    }
}
