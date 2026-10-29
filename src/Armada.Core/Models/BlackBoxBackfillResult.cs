namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a sweeper or backfill pass.
    /// </summary>
    public class BlackBoxBackfillResult
    {
        /// <summary>
        /// Sweeper or Backfill.
        /// </summary>
        public BlackBoxCaptureModeEnum Mode { get; set; } = BlackBoxCaptureModeEnum.Sweeper;

        /// <summary>
        /// Subjects examined.
        /// </summary>
        public int Scanned { get; set; } = 0;

        /// <summary>
        /// Entries captured.
        /// </summary>
        public int Captured { get; set; } = 0;

        /// <summary>
        /// Subjects skipped (already current).
        /// </summary>
        public int Skipped { get; set; } = 0;

        /// <summary>
        /// Captures that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// EnrichmentFailed entries requeued.
        /// </summary>
        public int EnrichmentRequeued { get; set; } = 0;

        /// <summary>
        /// Started (UTC).
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Finished (UTC).
        /// </summary>
        public DateTime? FinishedUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBackfillResult()
        {
        }
    }
}
