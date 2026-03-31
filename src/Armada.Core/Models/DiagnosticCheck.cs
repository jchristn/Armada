namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from GET /api/v1/doctor.
    /// </summary>
    public class DiagnosticCheck
    {
        /// <summary>
        /// Check name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Check status (Pass, Fail, Warn).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Detail message.
        /// </summary>
        public string? Message { get; set; }
    }
}
