namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Compact view of a black box entry (about 2 KB) for MCP get_blackbox format=summary: identity, where, how, status before and after, why, and the top recovery paths.
    /// </summary>
    public class BlackBoxDigest
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Mission or voyage.
        /// </summary>
        public BlackBoxSubjectTypeEnum SubjectType { get; set; } = BlackBoxSubjectTypeEnum.Mission;

        /// <summary>
        /// Mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Mission (or voyage) title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Attempt.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Capture mode (Sweeper and Backfill are lower fidelity).
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// State.
        /// </summary>
        public BlackBoxStateEnum State { get; set; } = BlackBoxStateEnum.Captured;

        /// <summary>
        /// Phase.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Host label.
        /// </summary>
        public string? Host { get; set; } = null;

        /// <summary>
        /// Source file of the failure site.
        /// </summary>
        public string? SourceFile { get; set; } = null;

        /// <summary>
        /// Source line.
        /// </summary>
        public int? SourceLine { get; set; } = null;

        /// <summary>
        /// Method.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Failure kind.
        /// </summary>
        public MissionFailureKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Reason excerpt (at most 500 characters).
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// One-line mechanism (exception, exit code, failing command or git operation).
        /// </summary>
        public string? Mechanism { get; set; } = null;

        /// <summary>
        /// Status before.
        /// </summary>
        public string? StatusBefore { get; set; } = null;

        /// <summary>
        /// Status after.
        /// </summary>
        public string? StatusAfter { get; set; } = null;

        /// <summary>
        /// Triage verdict.
        /// </summary>
        public BlackBoxTriageEnum Triage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Triage confidence, or null.
        /// </summary>
        public BlackBoxConfidenceEnum? TriageConfidence { get; set; } = null;

        /// <summary>
        /// Whether the ArmadaDefect verdict is suspected (rests on the handoff and landing prior alone).
        /// </summary>
        public bool TriageSuspected { get; set; } = false;

        /// <summary>
        /// Whether the failure is in Armada's handoff and landing scope.
        /// </summary>
        public bool ResilienceInScope { get; set; } = false;

        /// <summary>
        /// Whether Armada could have healed it by itself.
        /// </summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>
        /// What Armada could have done automatically, or null.
        /// </summary>
        public string? SelfHealingProposal { get; set; } = null;

        /// <summary>
        /// Why Armada did not catch it earlier, or null.
        /// </summary>
        public string? DetectionGap { get; set; } = null;

        /// <summary>
        /// Severity of the resilience section.
        /// </summary>
        public BlackBoxSeverityEnum ResilienceSeverity { get; set; } = BlackBoxSeverityEnum.Low;

        /// <summary>
        /// The most impactful proposed Armada change (its mission brief left out; read the Resilience section for it), or null.
        /// </summary>
        public BlackBoxProposedChange? TopProposedChange { get; set; } = null;

        /// <summary>
        /// Analysis summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Top why findings.
        /// </summary>
        [CollectionBound(3, Reason = "Top three findings")]
        public List<BlackBoxFinding> Why
        {
            get => _Why;
            set => _Why = value ?? new List<BlackBoxFinding>();
        }

        /// <summary>
        /// Top recovery paths.
        /// </summary>
        [CollectionBound(3, Reason = "Top three paths")]
        public List<BlackBoxRecoveryPath> RecoveryPaths
        {
            get => _RecoveryPaths;
            set => _RecoveryPaths = value ?? new List<BlackBoxRecoveryPath>();
        }

        /// <summary>
        /// Conflicted files.
        /// </summary>
        public int ConflictedFileTotal { get; set; } = 0;

        /// <summary>
        /// Objects.
        /// </summary>
        public int ObjectCount { get; set; } = 0;

        /// <summary>
        /// Resolution.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Open;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        private List<BlackBoxFinding> _Why = new List<BlackBoxFinding>();
        private List<BlackBoxRecoveryPath> _RecoveryPaths = new List<BlackBoxRecoveryPath>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDigest()
        {
        }
    }
}
