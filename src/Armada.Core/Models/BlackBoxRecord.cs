namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// The content of a black box entry's record.json: a versioned forensic snapshot of one mission or voyage failure, deserialized into typed classes (never read through JsonElement). A section that was not captured is null; readers render whatever sections exist. Every string is capped and every collection bounded (BlackBoxLimits).
    /// </summary>
    public class BlackBoxRecord
    {
        /// <summary>
        /// Entry identifier (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Record schema version (BlackBoxLimits.SchemaVersion for this build).
        /// </summary>
        public int SchemaVersion { get; set; } = BlackBoxLimits.SchemaVersion;

        /// <summary>
        /// Advisor rules version that produced the Analysis section (0 when not analyzed).
        /// </summary>
        public int RulesVersion { get; set; } = 0;

        /// <summary>
        /// How the entry was captured.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// Processing state when the record was last written.
        /// </summary>
        public BlackBoxStateEnum State { get; set; } = BlackBoxStateEnum.Captured;

        /// <summary>
        /// When the failure was captured (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Identity of the failed mission or voyage.
        /// </summary>
        public BlackBoxSubject? Subject { get; set; } = null;

        /// <summary>
        /// Status before and after, and the timeline.
        /// </summary>
        public BlackBoxStatusChange? Status { get; set; } = null;

        /// <summary>
        /// Where it failed.
        /// </summary>
        public BlackBoxSite? Site { get; set; } = null;

        /// <summary>
        /// How it failed.
        /// </summary>
        public BlackBoxMechanism? Mechanism { get; set; } = null;

        /// <summary>
        /// Kind, raw reason and suspected cause.
        /// </summary>
        public BlackBoxClassification? Classification { get; set; } = null;

        /// <summary>
        /// Dock, worktree, branch and target state; null until captured or enriched.
        /// </summary>
        public BlackBoxWorkspace? Workspace { get; set; } = null;

        /// <summary>
        /// Conflicted files, directories and conflicting missions; null when not a conflict or not yet enriched.
        /// </summary>
        public BlackBoxConflict? Conflict { get; set; } = null;

        /// <summary>
        /// Scoped, out-of-scope and boundary files; null when not applicable.
        /// </summary>
        public BlackBoxScope? Scope { get; set; } = null;

        /// <summary>
        /// Effective configuration that shaped the failure.
        /// </summary>
        public BlackBoxConfiguration? Configuration { get; set; } = null;

        /// <summary>
        /// Prior attempts and the rescue chain, oldest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxAttempts, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxAttempt> Attempts
        {
            get => _Attempts;
            set => _Attempts = value ?? new List<BlackBoxAttempt>();
        }

        /// <summary>
        /// Index of the evidence objects.
        /// </summary>
        public BlackBoxEvidenceIndex? Evidence { get; set; } = null;

        /// <summary>
        /// Advisor output; null until analyzed.
        /// </summary>
        public BlackBoxAnalysis? Analysis { get; set; } = null;

        /// <summary>
        /// Fingerprint and similar entries.
        /// </summary>
        public BlackBoxRelated? Related { get; set; } = null;

        /// <summary>
        /// Voyage roll-up (voyage entries only).
        /// </summary>
        public BlackBoxVoyageRollup? Voyage { get; set; } = null;

        /// <summary>
        /// Which redaction rules fired and how often (never the values).
        /// </summary>
        public BlackBoxRedaction Redaction { get; set; } = new BlackBoxRedaction();

        /// <summary>
        /// Fields that were cut, with their original sizes, and capture notes.
        /// </summary>
        public BlackBoxTruncation Truncation { get; set; } = new BlackBoxTruncation();

        private List<BlackBoxAttempt> _Attempts = new List<BlackBoxAttempt>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecord()
        {
        }
    }
}
