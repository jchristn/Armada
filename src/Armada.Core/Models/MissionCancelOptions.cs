namespace Armada.Core.Models
{
    using System;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;

    /// <summary>
    /// How <c>IAdmiralService.CancelMissionAsync</c> records a cancellation a caller makes for its own reason (for example
    /// the cancellation of a vessel's active missions when the vessel is deleted): the black box step to capture instead
    /// of OperatorTransition or OperatorStop, the failure kind and reason written on the mission row before the capture,
    /// and extra capture context. The cancel then records exactly one entry.
    /// </summary>
    public class MissionCancelOptions
    {
        /// <summary>
        /// Black box step to capture, or null for the default (OperatorStop when a running agent was stopped, otherwise
        /// OperatorTransition).
        /// </summary>
        public BlackBoxStepEnum? BlackBoxStep { get; set; } = null;

        /// <summary>
        /// Failure kind written on the mission row with the cancellation, or null to leave it.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Failure reason written on the mission row with the cancellation, or null to leave it.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Extra capture context (vessel, note, failure site), applied after the defaults, or null.
        /// </summary>
        public Action<BlackBoxCaptureContext>? ConfigureCapture { get; set; } = null;
    }
}
