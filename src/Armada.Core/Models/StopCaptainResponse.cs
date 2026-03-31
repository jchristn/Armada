namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from POST /api/v1/captains/{id}/stop.
    /// </summary>
    public class StopCaptainResponse
    {
        /// <summary>
        /// Operation status (e.g. "stopped").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the stopped captain (MCP tool only).
        /// </summary>
        public string? CaptainId { get; set; }
    }
}
