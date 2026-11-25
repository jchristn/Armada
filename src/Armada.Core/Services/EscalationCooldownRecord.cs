namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// When an escalation rule last fired for an entity, kept so its cooldown survives a restart.
    /// </summary>
    public class EscalationCooldownRecord
    {
        /// <summary>
        /// When the rule fired (UTC).
        /// </summary>
        public DateTime LastFiredUtc { get; set; } = DateTime.UtcNow;
    }
}
