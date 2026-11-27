namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What an escalated attempt learns from the attempt it replaces (RS-5.7): why the previous captain stopped, the
    /// tail of its output, the files it changed, and the black box rule hint for that failure. The trigger sets
    /// <see cref="Reason"/>, the kinds and <see cref="Step"/>; the escalation fills in the rest before the dock goes,
    /// and <see cref="Armada.Core.Services.EscalationMemoryRenderer"/> renders it into the mission's escalation note.
    /// </summary>
    public class EscalationMemory
    {
        /// <summary>
        /// Most changed file paths kept on the memory; the escalation note shows fewer.
        /// </summary>
        public const int MaxChangedFiles = 200;

        /// <summary>Why the previous captain could not finish, in one line.</summary>
        public string Reason
        {
            get => _Reason;
            set => _Reason = (value ?? String.Empty).Trim();
        }

        /// <summary>Mission failure kind of the stopped attempt, or null when none applies.</summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>Runtime classification of the agent's exit, or null when the attempt did not end in a failed exit.</summary>
        public RuntimeFailureKindEnum? RuntimeFailureKind { get; set; } = null;

        /// <summary>Agent process exit code, or null.</summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>Black box step the failure maps to; picks the rule hint.</summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>Name of the captain that stopped.</summary>
        public string? FromCaptainName { get; set; } = null;

        /// <summary>Model of the captain that stopped, or null when unset.</summary>
        public string? FromModel { get; set; } = null;

        /// <summary>The last part of the previous attempt's output, redacted, or null when there was none.</summary>
        public string? OutputTail { get; set; } = null;

        /// <summary>Files the previous attempt changed on the branch, or null when they could not be read.</summary>
        [CollectionBound(MaxChangedFiles, Reason = "MissionEscalationService keeps the first MaxChangedFiles paths")]
        public List<string>? ChangedFiles { get; set; } = null;

        /// <summary>Id of the black box rule behind <see cref="Hint"/>, or null.</summary>
        public string? HintRuleId { get; set; } = null;

        /// <summary>The black box rule's explanation of this failure, or null when no specific rule matched.</summary>
        public string? Hint { get; set; } = null;

        private string _Reason = String.Empty;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EscalationMemory()
        {
        }

        /// <summary>
        /// Instantiate for a trigger.
        /// </summary>
        /// <param name="reason">Why the previous captain could not finish.</param>
        /// <param name="step">Black box step of the failure.</param>
        /// <param name="failureKind">Mission failure kind, or null.</param>
        public EscalationMemory(string reason, BlackBoxStepEnum step, MissionFailureKindEnum? failureKind = null)
        {
            Reason = reason;
            Step = step;
            FailureKind = failureKind;
        }
    }
}
