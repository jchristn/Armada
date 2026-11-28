namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The one place that decides how a failure is recovered (RS-5.10): redispatch, resume in place, repair the output,
    /// rework, escalate up the ladder, rescue, wait or fail, from the failure's site and kind, the attempts already spent
    /// and the recovery policy. It only decides; the Admiral, mission service and recovery coordinator carry the actions
    /// out. Pure and thread-safe.
    /// </summary>
    public static class RecoveryPolicyEngine
    {
        /// <summary>
        /// Decide how to recover from a failure.
        /// </summary>
        /// <param name="facts">What is known about the failure.</param>
        /// <param name="policy">The recovery policy of the captain that failed, or null for a policy with no budgets.</param>
        /// <returns>The actions to try, in order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when facts is null.</exception>
        public static RecoveryDecision Decide(RecoveryFacts facts, RecoveryPolicy? policy = null)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            RecoveryPolicy p = policy ?? new RecoveryPolicy();
            RecoveryDecision decision = new RecoveryDecision();
            switch (facts.Site)
            {
                case RecoverySiteEnum.RuntimeExit:
                    DecideRuntimeExit(facts, p, decision);
                    break;
                case RecoverySiteEnum.ProcessLost:
                    if (Interrupted(facts)) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Redispatch, "interrupted"));
                    break;
                case RecoverySiteEnum.NoOpCompletion:
                    if (facts.Redispatches < facts.RedispatchLimit) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Redispatch));
                    else decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate));
                    break;
                case RecoverySiteEnum.OutputContract:
                    // A person decides review-gated stages; never re-run behind their back.
                    if (!facts.ReviewGated && facts.OutputRepairs < p.OutputRepairAttempts) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Repair));
                    break;
                case RecoverySiteEnum.JudgeRejection:
                    DecideJudgeRejection(facts, p, decision);
                    break;
                case RecoverySiteEnum.Incident:
                    if (facts.RescueInFlight) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Wait, "a rescue is in flight"));
                    else if (facts.Rescues < facts.RescueBudget && !facts.RescueLinksFull) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Rescue));
                    else return Fail(decision, "the rescue budget is used up");
                    return decision;
            }
            return Fail(decision, null);
        }

        private static void DecideRuntimeExit(RecoveryFacts facts, RecoveryPolicy policy, RecoveryDecision decision)
        {
            if (!facts.HasMission) return;

            // A negative exit code is an interruption (a stop, shutdown or vanished process), not an agent failure.
            if (Interrupted(facts))
            {
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Redispatch, "interrupted"));
                return;
            }

            RuntimeFailureKindEnum kind = facts.RuntimeFailureKind ?? RuntimeFailureKindEnum.Unclassified;
            bool transient = kind == RuntimeFailureKindEnum.Transient;

            // An endpoint whose breaker is open will not take this work soon (opt-in early fallback).
            if (facts.EndpointBreakerOpen && transient)
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate, "endpoint circuit breaker open"));

            // A model that emits a tool call the server cannot parse twice in a row rarely recovers on a resume.
            if (facts.RepeatedMalformedToolCalls)
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate, "repeated malformed tool calls"));

            // A model that runs out of iterations twice in a row is not converging; a fresh budget rarely helps (RS-5.12).
            if (facts.RepeatedIterationLimit)
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate, "repeated iteration limit"));

            // Transient and task failures resume in place up to MaxResumes; a transient outage keeps resuming within its
            // grace period. A task failure escalates after EscalateAfterResumes. An Unclassified exit gets its own, smaller
            // allowance so the budget is not spent on a cause nobody named.
            bool resumable = transient || kind == RuntimeFailureKindEnum.TaskFailure;
            bool unclassified = kind == RuntimeFailureKindEnum.Unclassified;
            bool unclassifiedResume = unclassified && facts.Resumes < policy.UnclassifiedResumes;
            bool resumesLeft = facts.Resumes < policy.MaxResumes && (transient || facts.Resumes < policy.EscalateAfterResumes);
            if ((resumable && (resumesLeft || facts.WithinOutageGrace)) || unclassifiedResume)
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Resume));

            // The resumes are used up and the captain still cannot finish: the next rung of the escalation ladder.
            if (resumable || unclassified) decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate));
        }

        private static void DecideJudgeRejection(RecoveryFacts facts, RecoveryPolicy policy, RecoveryDecision decision)
        {
            // An operator restart is an explicit decision to spend another round outside the automatic budget.
            if (facts.OperatorRestart)
            {
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Rework, "operator restart"));
                return;
            }

            // Unchanged rejected work, or the same affected case reported again, switches captain before the budget is spent.
            bool stagnant = (facts.ReworkSameCommit || facts.ReworkRepeatedCase != null) && !facts.WorkerReadOnly;
            if (facts.Escalations == 0 && facts.ReworkRounds < policy.JudgeReworkRounds && !stagnant)
            {
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Rework));
                return;
            }

            if (facts.Escalations >= facts.MaxEscalations)
            {
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Fail, "Automatic Judge rework limit reached after " + facts.ReworkRounds + " rework round(s)"
                    + (facts.Escalations > 0
                        ? ", including " + facts.Escalations + " escalation(s) (MaxEscalations " + facts.MaxEscalations + ")."
                        : "; escalation is off (MaxEscalations 0).")));
                return;
            }

            string trigger = facts.Escalations > 0
                ? "The escalated Worker's attempt was rejected too (" + facts.Escalations + " of " + facts.MaxEscalations + " escalations)."
                : facts.ReworkRounds >= policy.JudgeReworkRounds
                ? "Automatic Judge rework budget exhausted for this slice (" + facts.ReworkRounds + " of " + policy.JudgeReworkRounds + " rounds)."
                : facts.ReworkSameCommit
                    ? "The Worker produced the same rejected commit after " + facts.ReworkRounds + " rework round(s)."
                    : "The Judge reported the same affected case (" + facts.ReworkRepeatedCase + ") after " + facts.ReworkRounds + " rework round(s).";
            decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Escalate, trigger));
        }

        private static bool Interrupted(RecoveryFacts facts)
        {
            return facts.HasMission && facts.ExitCode.HasValue && facts.ExitCode.Value < 0
                && !facts.MissionCancelled && facts.Redispatches < facts.RedispatchLimit;
        }

        private static RecoveryDecision Fail(RecoveryDecision decision, string? reason)
        {
            if (decision.Steps.Count == 0 || decision.Steps[decision.Steps.Count - 1].Action != RecoveryActionEnum.Fail)
                decision.Steps.Add(new RecoveryStep(RecoveryActionEnum.Fail, reason));
            return decision;
        }
    }
}
