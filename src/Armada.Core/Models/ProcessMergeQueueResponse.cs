namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from POST /api/v1/merge-queue/process.
    /// </summary>
    public class ProcessMergeQueueResponse
    {
        /// <summary>
        /// Operation status (e.g. "processed").
        /// </summary>
        public string? Status { get; set; }
    }
}
