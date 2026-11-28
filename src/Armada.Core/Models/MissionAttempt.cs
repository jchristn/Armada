namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One attempt of a mission in its attempt history (GET /api/v1/missions/{id}/attempts): who ran it, how it ended,
    /// whether it was a repair, and where the mission went next. Built from the attempt's stage outcome.
    /// </summary>
    public class MissionAttempt
    {
        /// <summary>
        /// Stage outcome the attempt was read from (sto_ prefix).
        /// </summary>
        public string OutcomeId { get; set; } = "";

        /// <summary>
        /// Attempt number within the stage, from 1.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Judge rework round the attempt belongs to (0 for the first implementation).
        /// </summary>
        public int ReworkRound { get; set; } = 0;

        /// <summary>
        /// Captain that ran the attempt, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain name at the time of the attempt, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Runtime of the captain, or null.
        /// </summary>
        public AgentRuntimeEnum? Runtime { get; set; } = null;

        /// <summary>
        /// Model that ran the attempt, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Model provider, or null.
        /// </summary>
        public string? ModelProvider { get; set; } = null;

        /// <summary>
        /// Model endpoint (mep_ prefix), or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// How the attempt ended: Completed, Relaunched (resumed or rerun on the same captain), OutputRepair (a repair
        /// rerun), Escalated (moved to another captain), Failed, Interrupted, Cancelled or Pending.
        /// </summary>
        public StageDispositionEnum Disposition { get; set; } = StageDispositionEnum.Pending;

        /// <summary>
        /// Mission failure kind of the attempt, or null when it did not fail.
        /// </summary>
        public MissionFailureKindEnum? FailureKind { get; set; } = null;

        /// <summary>
        /// Failure reason text, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// How the runtime classified the exit (Clean, UsageLimit, AuthFailure, Crash, ModelUnavailable, TaskFailure,
        /// Transient or Unclassified), or null.
        /// </summary>
        public RuntimeFailureKindEnum? RuntimeFailureKind { get; set; } = null;

        /// <summary>
        /// Deterministic output-contract repair applied to the attempt's output, or null.
        /// </summary>
        public ContractRepairKindEnum? ContractRepairKind { get; set; } = null;

        /// <summary>
        /// True when the attempt was a repair: an output repair rerun, or a deterministic or format-only contract repair.
        /// </summary>
        public bool IsRepair { get; set; } = false;

        /// <summary>
        /// True when the mission escalated to another captain after this attempt.
        /// </summary>
        public bool Escalated { get; set; } = false;

        /// <summary>
        /// Captain the mission went to next (the escalation target when <see cref="Escalated"/>), or null.
        /// </summary>
        public string? NextCaptainId { get; set; } = null;

        /// <summary>
        /// Judge verdict of the attempt (PASS, FAIL, NEEDS_REVISION), or null.
        /// </summary>
        public string? JudgeVerdict { get; set; } = null;

        /// <summary>
        /// Suspected cause of a failed attempt.
        /// </summary>
        public FailureCauseEnum SuspectedCause { get; set; } = FailureCauseEnum.None;

        /// <summary>
        /// True for a battery or evaluation attempt.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;

        /// <summary>
        /// When the attempt started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When the attempt ended.
        /// </summary>
        public DateTime EndedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Duration in milliseconds, or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionAttempt()
        {
        }

        /// <summary>
        /// Build an attempt from its stage outcome.
        /// </summary>
        /// <param name="outcome">Stage outcome.</param>
        /// <returns>The attempt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcome is null.</exception>
        public static MissionAttempt FromOutcome(StageOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            bool contractRepair = outcome.ContractRepairKind.HasValue && outcome.ContractRepairKind.Value != ContractRepairKindEnum.None;
            return new MissionAttempt
            {
                OutcomeId = outcome.Id,
                Attempt = outcome.Attempt,
                ReworkRound = outcome.ReworkRound,
                CaptainId = outcome.CaptainId,
                CaptainName = outcome.CaptainName,
                Runtime = outcome.Runtime,
                Model = outcome.Model,
                ModelProvider = outcome.ModelProvider,
                ModelEndpointId = outcome.ModelEndpointId,
                Disposition = outcome.Disposition,
                FailureKind = outcome.FailureKind,
                FailureReason = outcome.FailureReason,
                RuntimeFailureKind = outcome.RuntimeFailureKind,
                ContractRepairKind = outcome.ContractRepairKind,
                IsRepair = outcome.Disposition == StageDispositionEnum.OutputRepair || contractRepair,
                Escalated = outcome.Disposition == StageDispositionEnum.Escalated,
                NextCaptainId = outcome.NextCaptainId,
                JudgeVerdict = outcome.JudgeVerdict,
                SuspectedCause = outcome.SuspectedCause,
                IsEvaluation = outcome.IsEvaluation,
                StartedUtc = outcome.StartedUtc,
                EndedUtc = outcome.EndedUtc,
                DurationMs = outcome.DurationMs
            };
        }
    }
}
