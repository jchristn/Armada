namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>Bounded ephemeral retry state; durable decisions remain the authoritative history.</summary>
    internal sealed class UsageSelectionTransientBlocker
    {
        internal string PolicyKey { get; set; } = string.Empty;
        internal DateTime RetryUtc { get; set; }
        internal MissionAssignmentBlocker Blocker { get; set; } = new MissionAssignmentBlocker();
    }
}
