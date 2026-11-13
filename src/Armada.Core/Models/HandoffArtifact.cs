namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// The durable, versioned record one stage attempt hands to the next stage (docs/MODEL_HETEROGENEITY.md, 3.1): the
    /// numbered findings, acceptance criteria, check results, commit references, the full stage output and the full diff
    /// (or a pointer to where the diff can be regenerated). The next stage receives a budgeted rendering of it at launch
    /// (<see cref="Armada.Core.Services.HandoffRenderer"/>) and a pointer to the complete artifact.
    /// </summary>
    public class HandoffArtifact
    {
        #region Public-Members

        /// <summary>
        /// Schema version written by this build. Readers handle every version up to it.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// Unique identifier (hoa_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Schema version of this artifact.
        /// </summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// Tenant of the mission.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner of the mission.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// The stage (mission) that produced the artifact.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Voyage of the mission, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel of the mission, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// The stage outcome recorded for the same attempt, or null when the artifact was built without one.
        /// </summary>
        public string? StageOutcomeId { get; set; } = null;

        /// <summary>
        /// Persona of the stage (Worker, Judge, Test Engineer, ...).
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Captain that ran the attempt, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain name when the attempt ran, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Attempt number on the mission (the stage outcome's attempt), starting at 1.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Rework round of the mission (0 for the first attempt of a slice).
        /// </summary>
        public int ReworkRound { get; set; } = 0;

        /// <summary>
        /// The stage's verdict or result line value (PASS, NEEDS_REVISION, COMPLETE, BLOCKED, ...), or null.
        /// </summary>
        public string? Verdict { get; set; } = null;

        /// <summary>
        /// Branch the stage worked on, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Commit the stage produced, or null.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Numbered findings, in the order the stage reported them.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "HandoffArtifactBuilder keeps the first entries and a final marker entry with the omitted count")]
        public List<HandoffFinding> Findings
        {
            get => _Findings;
            set => _Findings = value ?? new List<HandoffFinding>();
        }

        /// <summary>
        /// Acceptance criteria of the stage's task, as written in its description.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "HandoffArtifactBuilder keeps the first entries and a final marker entry with the omitted count")]
        public List<string> AcceptanceCriteria
        {
            get => _AcceptanceCriteria;
            set => _AcceptanceCriteria = value ?? new List<string>();
        }

        /// <summary>
        /// Actions the next stage must take (resolve findings, fix a failing check), most important first.
        /// </summary>
        [CollectionBound(8, Reason = "HandoffArtifactBuilder adds at most two finding actions, one specification-conflict action and one per check result")]
        public List<string> RequiredActions
        {
            get => _RequiredActions;
            set => _RequiredActions = value ?? new List<string>();
        }

        /// <summary>
        /// Results of the mechanical checks run after the stage (passed, failed, skipped with a reason, could not run).
        /// Empty when no check result was recorded.
        /// </summary>
        [CollectionBound(1, Reason = "One stage check result per stage attempt")]
        public List<StageCheckResult> CheckResults
        {
            get => _CheckResults;
            set => _CheckResults = value ?? new List<StageCheckResult>();
        }

        /// <summary>
        /// Commits the stage produced or cited: the produced commit first.
        /// </summary>
        [CollectionBound(20, Reason = "HandoffArtifactBuilder stops at 20 commit references")]
        public List<string> CommitReferences
        {
            get => _CommitReferences;
            set => _CommitReferences = value ?? new List<string>();
        }

        /// <summary>
        /// The protocol lines the stage emitted ([ARMADA:RESULT], [ARMADA:VERDICT], [ARMADA:STAGE-OUTCOME]).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "HandoffArtifactBuilder keeps the first lines and a final [ARMADA:TRUNCATED] marker with the omitted count")]
        public List<string> CompletionLines
        {
            get => _CompletionLines;
            set => _CompletionLines = value ?? new List<string>();
        }

        /// <summary>
        /// The full stage output, or null when none was recorded or it exceeded the stored maximum
        /// (<see cref="OutputComplete"/> is then false and the output remains on the mission).
        /// </summary>
        public string? Output { get; set; } = null;

        /// <summary>
        /// Length of the stage output in characters (0 when none was recorded).
        /// </summary>
        public int OutputLength { get; set; } = 0;

        /// <summary>
        /// Whether <see cref="Output"/> holds the whole stage output.
        /// </summary>
        public bool OutputComplete { get; set; } = true;

        /// <summary>
        /// The full diff of the stage, or null when none was recorded or it exceeded the stored maximum
        /// (<see cref="DiffPointer"/> then says where to regenerate it).
        /// </summary>
        public string? Diff { get; set; } = null;

        /// <summary>
        /// Length of the diff in characters (0 when none was recorded).
        /// </summary>
        public int DiffLength { get; set; } = 0;

        /// <summary>
        /// Whether <see cref="Diff"/> holds the whole diff.
        /// </summary>
        public bool DiffComplete { get; set; } = true;

        /// <summary>
        /// Where the complete diff can be regenerated (the branch and commit, and the mission diff API).
        /// </summary>
        public string? DiffPointer { get; set; } = null;

        /// <summary>
        /// When the artifact was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the artifact was last written (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.HandoffArtifactIdPrefix, 24);
        private List<HandoffFinding> _Findings = new List<HandoffFinding>();
        private List<string> _AcceptanceCriteria = new List<string>();
        private List<string> _RequiredActions = new List<string>();
        private List<StageCheckResult> _CheckResults = new List<StageCheckResult>();
        private List<string> _CommitReferences = new List<string>();
        private List<string> _CompletionLines = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HandoffArtifact()
        {
        }

        #endregion
    }
}
