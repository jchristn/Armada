namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.capture_failed event.
    /// </summary>
    public class BlackBoxCaptureFailedPayload
    {
        /// <summary>
        /// Mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Step.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Error (redacted).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCaptureFailedPayload()
        {
        }
    }
}
