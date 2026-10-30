namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of deleting black box entries.
    /// </summary>
    public class BlackBoxDeleteResult
    {
        /// <summary>
        /// Ids requested.
        /// </summary>
        public int Requested { get; set; } = 0;

        /// <summary>
        /// Entries deleted.
        /// </summary>
        public int Deleted { get; set; } = 0;

        /// <summary>
        /// Entries left in the Deleting state because a store delete failed (retried by garbage collection).
        /// </summary>
        public int Pending { get; set; } = 0;

        /// <summary>
        /// Stored bytes freed.
        /// </summary>
        public long BytesFreed { get; set; } = 0;

        /// <summary>
        /// Ids not deleted (missing, other tenant, or pending).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDeleteIds, Reason = "Bounded by the request")]
        public List<string> Skipped
        {
            get => _Skipped;
            set => _Skipped = value ?? new List<string>();
        }

        private List<string> _Skipped = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDeleteResult()
        {
        }
    }
}
