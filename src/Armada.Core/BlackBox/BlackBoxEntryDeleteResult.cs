namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of deleting one black box entry (two-phase: mark Deleting, delete external bytes, delete rows).
    /// </summary>
    public class BlackBoxEntryDeleteResult
    {
        /// <summary>
        /// Whether every row was deleted.
        /// </summary>
        public bool Deleted { get; set; } = false;

        /// <summary>
        /// Whether the entry was left Deleting because a store delete failed (retried by garbage collection).
        /// </summary>
        public bool Pending { get; set; } = false;

        /// <summary>
        /// Stored bytes freed.
        /// </summary>
        public long BytesFreed { get; set; } = 0;

        /// <summary>
        /// Store error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEntryDeleteResult()
        {
        }
    }
}
