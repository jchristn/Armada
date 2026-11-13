namespace Armada.Core.Models
{
    using Armada.Core.Enums;
    /// <summary>One continuous interval during which a mission waits for a scheduling gate.</summary>
    public sealed class MissionWaitInterval
    {
        /// <summary>Stable interval identifier.</summary>
        public string Id { get; set; } = Constants.IdGenerator.GenerateKSortable(Constants.MissionWaitIntervalIdPrefix, 24);
        /// <summary>Owning tenant identifier.</summary>
        public string? TenantId { get; set; }
        /// <summary>Mission whose progress is blocked.</summary>
        public string MissionId { get; set; } = "";
        /// <summary>Vessel at the time the wait began.</summary>
        public string? VesselId { get; set; }
        /// <summary>Voyage at the time the wait began.</summary>
        public string? VoyageId { get; set; }
        /// <summary>Captain associated with this wait, when known.</summary>
        public string? CaptainId { get; set; }
        /// <summary>Gate preventing progress.</summary>
        public MissionWaitGateEnum Gate { get; set; }
        /// <summary>UTC time the wait began.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>UTC time the gate cleared; null while still waiting.</summary>
        public DateTime? EndedUtc { get; set; }
        /// <summary>Diagnostic context for the wait.</summary>
        public string? Detail { get; set; }
    }
}
