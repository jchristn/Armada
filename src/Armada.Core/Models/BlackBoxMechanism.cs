namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Mechanism section: how a failure happened.
    /// </summary>
    public class BlackBoxMechanism
    {
        /// <summary>
        /// Exception type, or null.
        /// </summary>
        public string? ExceptionType { get; set; } = null;

        /// <summary>
        /// Exception message (redacted, at most 4000 characters).
        /// </summary>
        public string? ExceptionMessage { get; set; } = null;

        /// <summary>
        /// Armada stack frames only.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxStackFrames, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> StackFrames
        {
            get => _StackFrames;
            set => _StackFrames = value ?? new List<string>();
        }

        /// <summary>
        /// Agent exit code, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// How the runtime classified the exit, or null.
        /// </summary>
        public RuntimeFailureKindEnum? RuntimeFailureKind { get; set; } = null;

        /// <summary>
        /// Failing command, or null.
        /// </summary>
        public string? FailingCommand { get; set; } = null;

        /// <summary>
        /// Failing command's exit code, or null.
        /// </summary>
        public int? CommandExitCode { get; set; } = null;

        /// <summary>
        /// Tail of the failing command's output (at most 8000 characters).
        /// </summary>
        public string? CommandOutputTail { get; set; } = null;

        /// <summary>
        /// Git operation that failed, or null.
        /// </summary>
        public string? GitOperation { get; set; } = null;

        /// <summary>
        /// Tail of git stderr (at most 4000 characters).
        /// </summary>
        public string? GitStderrTail { get; set; } = null;

        private List<string> _StackFrames = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxMechanism()
        {
        }
    }
}
