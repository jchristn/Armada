namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Short form of a black box entry for lists.
    /// </summary>
    public class BlackBoxEntrySummary
    {
        /// <summary>
        /// Entry identifier.
        /// </summary>
        public string Id { get; set; } = "";

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
        /// Vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission (or voyage) title.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Attempt.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Whether current.
        /// </summary>
        public bool IsCurrent { get; set; } = false;

        /// <summary>
        /// Phase.
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Failure kind.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Status before.
        /// </summary>
        public string? StatusBefore { get; set; } = null;

        /// <summary>
        /// Status after.
        /// </summary>
        public string? StatusAfter { get; set; } = null;

        /// <summary>
        /// Triage.
        /// </summary>
        public BlackBoxTriageEnum Triage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Fingerprint.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Capture mode.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// State.
        /// </summary>
        public BlackBoxStateEnum State { get; set; } = BlackBoxStateEnum.Captured;

        /// <summary>
        /// Resolution.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Open;

        /// <summary>
        /// Pinned.
        /// </summary>
        public bool Pinned { get; set; } = false;

        /// <summary>
        /// Reason excerpt (at most 300 characters).
        /// </summary>
        public string? ReasonExcerpt { get; set; } = null;

        /// <summary>
        /// Stored bytes.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Objects.
        /// </summary>
        public int ObjectCount { get; set; } = 0;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expires (UTC).
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEntrySummary()
        {
        }
    }
}
