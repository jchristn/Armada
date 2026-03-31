namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from POST /api/v1/server/stop.
    /// </summary>
    public class StopServerResponse
    {
        /// <summary>
        /// Operation status (e.g. "shutting_down").
        /// </summary>
        public string? Status { get; set; }
    }
}
