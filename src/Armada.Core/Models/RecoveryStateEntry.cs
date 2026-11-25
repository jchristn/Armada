namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One piece of recovery state that must survive an Admiral restart (RELIABILITY_SIMPLICITY_PLAN.md RS-3.7): an
    /// endpoint circuit breaker, a mission's launch backoff, the start of a transient outage, or an escalation cooldown.
    /// </summary>
    public class RecoveryStateEntry
    {
        /// <summary>
        /// Kind of state; one of <see cref="RecoveryStateKinds"/>.
        /// </summary>
        public string Kind { get; set; } = "";

        /// <summary>
        /// Key within the kind (endpoint, mission id, or escalation rule and entity).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// State, serialized as JSON.
        /// </summary>
        public string PayloadJson { get; set; } = "{}";

        /// <summary>
        /// When the state no longer matters and may be purged (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the state was last written (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
