namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/missions/{id} or armada_purge_mission.
    /// </summary>
    public class DeleteMissionResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted mission.
        /// </summary>
        public string? MissionId { get; set; }
    }
}
