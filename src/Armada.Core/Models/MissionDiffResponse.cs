namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/missions/{id}/diff.
    /// </summary>
    public class MissionDiffResponse
    {
        /// <summary>
        /// Mission ID.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Branch name.
        /// </summary>
        public string? Branch { get; set; }

        /// <summary>
        /// The diff content.
        /// </summary>
        public string? Diff { get; set; }

        /// <summary>
        /// Error message if diff retrieval failed.
        /// </summary>
        public string? Error { get; set; }
    }
}
