namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/captains/{id}/log.
    /// </summary>
    public class CaptainLogResponse
    {
        /// <summary>
        /// Captain ID.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Log content.
        /// </summary>
        public string? Log { get; set; }

        /// <summary>
        /// Number of lines returned.
        /// </summary>
        public int Lines { get; set; }

        /// <summary>
        /// Total number of lines in the log file.
        /// </summary>
        public int TotalLines { get; set; }

        /// <summary>
        /// Error message if log retrieval failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Error description.
        /// </summary>
        public string? Description { get; set; }
    }
}
