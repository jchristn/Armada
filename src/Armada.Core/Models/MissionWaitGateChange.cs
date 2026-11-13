namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Payload of the <c>mission.waiting_gate_changed</c> WebSocket event: a Pending mission started waiting on a
    /// different gate. Sent once per gate change, never per scheduler tick.
    /// </summary>
    public class MissionWaitGateChange
    {
        /// <summary>Tenant that owns the mission.</summary>
        public string? TenantId { get; set; }

        /// <summary>The waiting mission.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>The mission's voyage, if any.</summary>
        public string? VoyageId { get; set; }

        /// <summary>The mission's vessel, if any.</summary>
        public string? VesselId { get; set; }

        /// <summary>The gate the mission now waits on.</summary>
        public MissionWaitGateEnum Gate { get; set; } = MissionWaitGateEnum.Other;

        /// <summary>The gate it waited on before, or null when the wait just began.</summary>
        public MissionWaitGateEnum? PreviousGate { get; set; }

        /// <summary>Selection or blocker detail recorded with the wait.</summary>
        public string? Detail { get; set; }

        /// <summary>When the mission started waiting on <see cref="Gate"/>.</summary>
        public DateTime SinceUtc { get; set; }
    }
}
