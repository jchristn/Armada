namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>Scheduling metadata only: never loads mission outputs, diffs or evidence.</summary>
    public sealed class MissionAdmissionSnapshot
    {
        /// <summary>Mission identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Voyage identifier, if any.</summary>
        public string? VoyageId { get; set; }
        /// <summary>Preceding standalone pipeline stage, if any.</summary>
        public string? DependsOnMissionId { get; set; }
        /// <summary>Mission state.</summary>
        public MissionStatusEnum Status { get; set; }
        /// <summary>Current parent voyage state, if a parent exists.</summary>
        public VoyageStatusEnum? VoyageStatus { get; set; }

        /// <summary>Project an in-memory mission without carrying its artifacts into admission.</summary>
        public static MissionAdmissionSnapshot FromMission(Mission mission) => new MissionAdmissionSnapshot
        {
            Id = mission.Id, VoyageId = mission.VoyageId, DependsOnMissionId = mission.DependsOnMissionId, Status = mission.Status
        };
    }
}
