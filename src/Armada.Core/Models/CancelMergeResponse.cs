namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from armada_cancel_merge MCP tool.
    /// </summary>
    public class CancelMergeResponse
    {
        /// <summary>
        /// Operation status (e.g. "cancelled").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the cancelled entry.
        /// </summary>
        public string? EntryId { get; set; }
    }
}
