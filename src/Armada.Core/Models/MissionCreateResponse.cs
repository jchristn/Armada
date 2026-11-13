namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Wrapper that servers before v1.3.0 returned when POST /api/v1/missions created a mission that stayed Pending.
    /// Current servers return the Mission with an X-Armada-Warning header instead; clients read both shapes.
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
