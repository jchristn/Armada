namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Standard error response from the API.
    /// </summary>
    public class ArmadaErrorResponse
    {
        /// <summary>
        /// Error code or category (e.g. "NotFound", "Conflict").
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Human-readable error description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string? Message { get; set; }
    }
}
