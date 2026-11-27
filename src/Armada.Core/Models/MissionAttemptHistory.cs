namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A mission's attempt history (GET /api/v1/missions/{id}/attempts, MCP get_mission_attempts): the mission's
    /// recovery counters and escalation state, and every recorded attempt, oldest first.
    /// </summary>
    public class MissionAttemptHistory
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
        /// Persona the mission runs as, or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Mission mode.
        /// </summary>
        public MissionModeEnum Mode { get; set; } = MissionModeEnum.Implementation;

        /// <summary>
        /// Current status.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Current captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Effective tier the mission was last routed at, or null.
        /// </summary>
        public CaptainTierEnum? Tier { get; set; } = null;

        /// <summary>
        /// Escalation hops taken so far.
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
        /// True when Judge rework moved the work to a different Worker captain.
        /// </summary>
        public bool ReworkEscalated { get; set; } = false;

        /// <summary>
        /// Redispatches (resumes and reruns) so far.
        /// </summary>
        public int RedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Output repair reruns so far.
        /// </summary>
        public int OutputRepairAttempts { get; set; } = 0;

        /// <summary>
        /// No-op redispatches so far.
        /// </summary>
        public int NoOpRedispatchAttempts { get; set; } = 0;

        /// <summary>
        /// Stall recoveries so far.
        /// </summary>
        public int StallRecoveryAttempts { get; set; } = 0;

        /// <summary>
        /// Recorded attempts, oldest first.
        /// </summary>
        public List<MissionAttempt> Attempts { get; set; } = new List<MissionAttempt>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionAttemptHistory()
        {
        }
    }
}
