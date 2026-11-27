namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// WebSocket payload of mission.escalated, mission.repaired and mission.attempt_changed: the mission's recovery
    /// counters and escalation state after the change. Scoped like mission.changed; clients re-fetch detail through
    /// GET /api/v1/missions/{id}/attempts.
    /// </summary>
    public class MissionAttemptChange
    {
        /// <summary>
        /// Mission (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Voyage, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Mission status after the change.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Captain after the change, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Persona, or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Effective tier, or null.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Escalation hops taken.
        /// </summary>
        public int EscalationCount { get; set; } = 0;

        /// <summary>
        /// Captain of the last escalation's source, or null.
        /// </summary>
        public string? EscalatedFromCaptainId { get; set; } = null;

        /// <summary>
        /// Model of the last escalation's source, or null.
        /// </summary>
        public string? EscalatedFromModel { get; set; } = null;

        /// <summary>
        /// Redispatches (resumes and reruns).
        /// </summary>
        public int RedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Output repair reruns.
        /// </summary>
        public int OutputRepairAttempts { get; set; } = 0;

        /// <summary>
        /// No-op redispatches.
        /// </summary>
        public int NoOpRedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Stall recoveries.
        /// </summary>
        public int StallRecoveryAttempts { get; set; } = 0;

        /// <summary>
        /// Build the payload from a mission.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>The payload.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when mission is null.</exception>
        public static MissionAttemptChange From(Mission mission)
        {
            if (mission == null) throw new System.ArgumentNullException(nameof(mission));
            return new MissionAttemptChange
            {
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                Status = mission.Status,
                CaptainId = mission.CaptainId,
                Persona = mission.Persona,
                Tier = mission.Tier,
                EscalationCount = mission.EscalationCount,
                EscalatedFromCaptainId = mission.EscalatedFromCaptainId,
                EscalatedFromModel = mission.EscalatedFromModel,
                RedispatchAttempts = mission.RedispatchAttempts,
                OutputRepairAttempts = mission.OutputRepairAttempts,
                NoOpRedispatchAttempts = mission.NoOpRedispatchAttempts,
                StallRecoveryAttempts = mission.StallRecoveryAttempts
            };
        }
    }
}
