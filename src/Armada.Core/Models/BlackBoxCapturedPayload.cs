namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.captured event.
    /// </summary>
    public class BlackBoxCapturedPayload
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

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
        public MissionFailureKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Attempt.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Capture mode.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCapturedPayload()
        {
        }
    }
}
