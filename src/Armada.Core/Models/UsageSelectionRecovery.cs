namespace Armada.Core.Models
{
    using System;

    /// <summary>Immutable, mission-scoped administrator authorization. A revision supersedes permission without rewriting history.</summary>
    public sealed class UsageSelectionRecovery
    {
        /// <summary>Audit identity.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Owning mission.</summary>
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Administrator who authorized the revision.</summary>
        public string ActorUserId { get; set; } = String.Empty;
        /// <summary>Operator explanation, required for either granting or revoking permissions.</summary>
        public string Reason { get; set; } = String.Empty;
        /// <summary>Allow ordinary eligible fallback from a preferred captain; strict assignments remain strict.</summary>
        public bool AllowFallback { get; set; }
        /// <summary>Allow this mission below the soft reserve; exhausted quota, per-attempt budget and cooldown still block.</summary>
        public bool ReserveException { get; set; }
        /// <summary>Monotonically increasing mission-scoped revision, starting at one.</summary>
        public int Revision { get; set; }
        /// <summary>UTC authorization time.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
