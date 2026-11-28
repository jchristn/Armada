namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Reliability of one (model, persona) pair over the summary window, counted from stage outcomes (one per attempt).
    /// </summary>
    public class ReliabilitySummaryRow
    {
        /// <summary>
        /// Model, or "(unknown)" when the attempt recorded none.
        /// </summary>
        public string Model { get; set; } = "";

        /// <summary>
        /// Persona, or "(none)" when the mission had none.
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Attempts recorded.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Attempts that completed.
        /// </summary>
        public int Completed { get; set; } = 0;

        /// <summary>
        /// Attempts that failed for good.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Attempts resumed or rerun on the same captain.
        /// </summary>
        public int Relaunched { get; set; } = 0;

        /// <summary>
        /// Attempts that were repairs (output repair reruns and deterministic or format-only contract repairs).
        /// </summary>
        public int Repairs { get; set; } = 0;

        /// <summary>
        /// Attempts after which the mission escalated to another captain.
        /// </summary>
        public int Escalations { get; set; } = 0;

        /// <summary>
        /// Attempts whose exit the runtime could not classify (RuntimeFailureKind Unclassified).
        /// </summary>
        public int Unclassified { get; set; } = 0;

        /// <summary>
        /// Completed divided by finished attempts (Completed, Failed, Escalated), 0 to 1; 0 when none finished.
        /// </summary>
        public double CompletionRate { get; set; } = 0;

        /// <summary>
        /// Unclassified divided by attempts, 0 to 1.
        /// </summary>
        public double UnclassifiedRate { get; set; } = 0;

        /// <summary>
        /// Count per mission failure kind (Compile, TestFail, NoOp, InvalidOutput, ...).
        /// </summary>
        [CollectionBound(64, Reason = "One per mission failure kind")]
        public Dictionary<string, int> FailureKinds { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Count per runtime failure kind (UsageLimit, Crash, Transient, Unclassified, ...), Clean excluded.
        /// </summary>
        [CollectionBound(64, Reason = "One per runtime failure kind")]
        public Dictionary<string, int> RuntimeFailureKinds { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Median attempt duration in milliseconds, or null when none recorded one.
        /// </summary>
        public long? MedianDurationMs { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReliabilitySummaryRow()
        {
        }
    }
}
