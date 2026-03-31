namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Wrapper returned when POST /api/v1/missions creates a mission that stays Pending.
    /// </summary>
    public class MissionCreateResponse
    {
        /// <summary>
        /// The created mission.
        /// </summary>
        public Mission? Mission { get; set; }

        /// <summary>
        /// Optional warning (e.g. no captain available).
        /// </summary>
        public string? Warning { get; set; }
    }
}
