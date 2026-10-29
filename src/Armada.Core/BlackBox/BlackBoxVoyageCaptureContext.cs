namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Everything a voyage failure site knows, handed to IBlackBoxCapture.CaptureVoyageFailureAsync.
    /// </summary>
    public class BlackBoxVoyageCaptureContext
    {
        /// <summary>
        /// The voyage after the failure write (required).
        /// </summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// Status before the failure write.
        /// </summary>
        public VoyageStatusEnum? StatusBefore { get; set; } = null;

        /// <summary>
        /// Failure step (a Voyage phase step).
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.SettlementFailed;

        /// <summary>
        /// Capture mode.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// Why the voyage failed, or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Exception, or null.
        /// </summary>
        public Exception? Exception { get; set; } = null;

        /// <summary>
        /// The failed mission at the root of the cascade, or null (the capture picks the first failed mission).
        /// </summary>
        public string? RootMissionId { get; set; } = null;

        /// <summary>
        /// Settlement rule that decided the voyage, or null.
        /// </summary>
        public string? SettlementRule { get; set; } = null;

        /// <summary>
        /// Halt reason, or null.
        /// </summary>
        public string? HaltReason { get; set; } = null;

        /// <summary>
        /// Free-text capture note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Calling method ([CallerMemberName]).
        /// </summary>
        public string? CallerMember { get; set; } = null;

        /// <summary>
        /// Calling source file ([CallerFilePath]).
        /// </summary>
        public string? CallerFile { get; set; } = null;

        /// <summary>
        /// Calling line ([CallerLineNumber]).
        /// </summary>
        public int? CallerLine { get; set; } = null;

        /// <summary>
        /// Component (class) name.
        /// </summary>
        public string? Component { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxVoyageCaptureContext()
        {
        }
    }
}
