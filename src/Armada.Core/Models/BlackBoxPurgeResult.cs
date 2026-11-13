namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a black box purge.
    /// </summary>
    public class BlackBoxPurgeResult
    {
        /// <summary>
        /// Whether this was a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Entries that matched the filter.
        /// </summary>
        public long Matched { get; set; } = 0;

        /// <summary>
        /// Entries deleted (0 for a dry run).
        /// </summary>
        public int Deleted { get; set; } = 0;

        /// <summary>
        /// Entries left Deleting after a store failure.
        /// </summary>
        public int Pending { get; set; } = 0;

        /// <summary>
        /// Pinned entries skipped.
        /// </summary>
        public int SkippedPinned { get; set; } = 0;

        /// <summary>
        /// Stored bytes freed (or that would be freed).
        /// </summary>
        public long BytesFreed { get; set; } = 0;

        /// <summary>
        /// Up to 100 of the matched entry ids.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxSampleIds, Reason = "Sample of the matches")]
        public List<string> SampleEntryIds
        {
            get => _SampleEntryIds;
            set => _SampleEntryIds = value ?? new List<string>();
        }

        private List<string> _SampleEntryIds = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxPurgeResult()
        {
        }
    }
}
