namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Report of one black box garbage collection run (also the blackbox.gc_completed event payload).
    /// </summary>
    public class BlackBoxGcReport
    {
        /// <summary>
        /// Whether this was a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Started (UTC).
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Finished (UTC).
        /// </summary>
        public DateTime? FinishedUtc { get; set; } = null;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Entries expired by age.
        /// </summary>
        public int Expired { get; set; } = 0;

        /// <summary>
        /// Entries evicted by quota.
        /// </summary>
        public int Evicted { get; set; } = 0;

        /// <summary>
        /// Entries deleted with their vessel or mission.
        /// </summary>
        public int Cascaded { get; set; } = 0;

        /// <summary>
        /// Entries left Deleting by an earlier run and finished now.
        /// </summary>
        public int RetriedDeletes { get; set; } = 0;

        /// <summary>
        /// Orphaned external objects removed.
        /// </summary>
        public int OrphansRemoved { get; set; } = 0;

        /// <summary>
        /// Objects found missing.
        /// </summary>
        public int ObjectsMissing { get; set; } = 0;

        /// <summary>
        /// Stored bytes freed.
        /// </summary>
        public long BytesFreed { get; set; } = 0;

        /// <summary>
        /// Entries whose delete failed (left Deleting, retried next run).
        /// </summary>
        public int Failures { get; set; } = 0;

        /// <summary>
        /// Up to 100 entry ids deleted (or that a dry run would delete).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxSampleIds, Reason = "Sample of the candidates")]
        public List<string> CandidateEntryIds
        {
            get => _CandidateEntryIds;
            set => _CandidateEntryIds = value ?? new List<string>();
        }

        /// <summary>
        /// Up to 20 error messages.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxErrors, Reason = "First errors only")]
        public List<string> Errors
        {
            get => _Errors;
            set => _Errors = value ?? new List<string>();
        }

        private List<string> _CandidateEntryIds = new List<string>();
        private List<string> _Errors = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxGcReport()
        {
        }
    }
}
