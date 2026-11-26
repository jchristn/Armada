namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="RecoveryPolicyEngine"/> (RS-5.10): one engine chooses redispatch, resume, repair, rework,
    /// escalate, rescue, wait or fail for every recovery site, from the failure kind, the attempts spent and the policy.
    /// </summary>
    public sealed class RecoveryPolicyEngineSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the recovery policy engine suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("interruption_redispatches_until_limit", "A negative exit redispatches while the budget lasts, unless cancelled", TestTags.Positive, () =>
            {
                RecoveryFacts facts = Exit(RuntimeFailureKindEnum.Crash, -1);
                facts.RedispatchLimit = 3;
                AssertEqual("Redispatch, Fail", Plan(facts));
                facts.MissionCancelled = true;
                AssertEqual("Fail", Plan(facts), "a cancelled mission is not redispatched");
                facts.MissionCancelled = false;
                facts.Redispatches = 3;
                AssertEqual("Fail", Plan(facts), "the budget is spent");
                facts.Site = RecoverySiteEnum.ProcessLost;
                facts.Redispatches = 0;
                AssertEqual("Redispatch, Fail", Plan(facts));
                facts.ExitCode = 1;
                AssertEqual("Fail", Plan(facts), "a lost process with a positive exit only fails");
            }));

            cases.Add(Case("task_failure_resumes_then_escalates_then_fails", "A task failure resumes in place, then escalates, then fails", TestTags.Positive, () =>
            {
                RecoveryPolicy policy = Policy();
                RecoveryFacts facts = Exit(RuntimeFailureKindEnum.TaskFailure, 1);
                AssertEqual("Resume, Escalate, Fail", Plan(facts, policy));
                facts.Resumes = 3;
                AssertEqual("Escalate, Fail", Plan(facts, policy));
                policy.EscalateAfterResumes = 1;
                facts.Resumes = 1;
                AssertEqual("Escalate, Fail", Plan(facts, policy), "EscalateAfterResumes skips the remaining resumes");
            }));

            cases.Add(Case("transient_keeps_resuming_in_grace", "A transient outage ignores EscalateAfterResumes and resumes within its grace", TestTags.Positive, () =>
            {
                RecoveryPolicy policy = Policy();
                policy.EscalateAfterResumes = 0;
                RecoveryFacts facts = Exit(RuntimeFailureKindEnum.Transient, 1);
                facts.Resumes = 1;
                AssertEqual("Resume, Escalate, Fail", Plan(facts, policy));
                facts.Resumes = 3;
                AssertEqual("Escalate, Fail", Plan(facts, policy));
                facts.WithinOutageGrace = true;
                AssertEqual("Resume, Escalate, Fail", Plan(facts, policy));
                facts.EndpointBreakerOpen = true;
                RecoveryDecision decision = RecoveryPolicyEngine.Decide(facts, policy);
                AssertEqual("Escalate, Resume, Escalate, Fail", Plan(facts, policy), "an open breaker escalates first");
                AssertEqual("endpoint circuit breaker open", decision.First.Reason);
            }));

            cases.Add(Case("unclassified_gets_its_own_allowance", "An Unclassified exit resumes only within UnclassifiedResumes", TestTags.Positive, () =>
            {
                RecoveryPolicy policy = Policy();
                RecoveryFacts facts = Exit(RuntimeFailureKindEnum.Unclassified, 1);
                AssertEqual("Resume, Escalate, Fail", Plan(facts, policy));
                facts.Resumes = 1;
                AssertEqual("Escalate, Fail", Plan(facts, policy));
            }));

            cases.Add(Case("provider_conditions_fail_at_once", "Usage limits, auth failures and crashes are not resumed or escalated", TestTags.Negative, () =>
            {
                foreach (RuntimeFailureKindEnum kind in new[] { RuntimeFailureKindEnum.UsageLimit, RuntimeFailureKindEnum.AuthFailure, RuntimeFailureKindEnum.Crash })
                    AssertEqual("Fail", Plan(Exit(kind, 1), Policy()), kind.ToString());
                RecoveryFacts none = Exit(RuntimeFailureKindEnum.TaskFailure, 1);
                none.HasMission = false;
                AssertEqual("Fail", Plan(none, Policy()), "no mission to recover");
            }));

            cases.Add(Case("malformed_tool_calls_escalate_first", "Repeated malformed tool calls escalate before resuming", TestTags.Positive, () =>
            {
                RecoveryFacts facts = Exit(RuntimeFailureKindEnum.TaskFailure, 1);
                facts.RepeatedMalformedToolCalls = true;
                RecoveryDecision decision = RecoveryPolicyEngine.Decide(facts, Policy());
                AssertEqual("Escalate, Resume, Escalate, Fail", Plan(facts, Policy()));
                AssertEqual("repeated malformed tool calls", decision.First.Reason);
            }));

            cases.Add(Case("noop_redispatches_then_escalates", "A no-op completion redispatches, then escalates, then fails", TestTags.Positive, () =>
            {
                RecoveryFacts facts = new RecoveryFacts(RecoverySiteEnum.NoOpCompletion) { RedispatchLimit = 2, Redispatches = 1 };
                AssertEqual("Redispatch, Fail", Plan(facts));
                facts.Redispatches = 2;
                AssertEqual("Escalate, Fail", Plan(facts));
            }));

            cases.Add(Case("output_repair_within_budget", "Output repair runs within its budget and never on review-gated stages", TestTags.Positive, () =>
            {
                RecoveryFacts facts = new RecoveryFacts(RecoverySiteEnum.OutputContract);
                AssertEqual("Repair, Fail", Plan(facts, Policy()));
                facts.OutputRepairs = 1;
                AssertEqual("Fail", Plan(facts, Policy()));
                facts.OutputRepairs = 0;
                facts.ReviewGated = true;
                AssertEqual("Fail", Plan(facts, Policy()));
                AssertEqual("Fail", Plan(new RecoveryFacts(RecoverySiteEnum.OutputContract)), "no policy means no repair budget");
            }));

            cases.Add(Case("judge_rejection_reworks_then_escalates", "Judge rejection reworks within budget, escalates past it, and stops at MaxEscalations", TestTags.Positive, () =>
            {
                RecoveryPolicy policy = Policy();
                RecoveryFacts facts = new RecoveryFacts(RecoverySiteEnum.JudgeRejection) { MaxEscalations = 2, ReworkRounds = 1 };
                AssertEqual("Rework, Fail", Plan(facts, policy));
                facts.ReworkRounds = 2;
                RecoveryDecision decision = RecoveryPolicyEngine.Decide(facts, policy);
                AssertEqual(RecoveryActionEnum.Escalate, decision.First.Action);
                AssertEqual("Automatic Judge rework budget exhausted for this slice (2 of 2 rounds).", decision.First.Reason);
                facts.Escalations = 1;
                AssertContains("rejected too (1 of 2 escalations)", RecoveryPolicyEngine.Decide(facts, policy).First.Reason!);
                facts.Escalations = 2;
                decision = RecoveryPolicyEngine.Decide(facts, policy);
                AssertEqual("Fail", Plan(facts, policy));
                AssertContains("including 2 escalation(s) (MaxEscalations 2)", decision.First.Reason!);
                facts.OperatorRestart = true;
                AssertEqual("Rework, Fail", Plan(facts, policy), "an operator restart spends a round outside the budget");
            }));

            cases.Add(Case("stagnant_rework_escalates_early", "Unchanged rejected work escalates before the budget, unless the Worker is read-only", TestTags.Positive, () =>
            {
                RecoveryFacts facts = new RecoveryFacts(RecoverySiteEnum.JudgeRejection) { MaxEscalations = 2, ReworkRounds = 1, ReworkSameCommit = true };
                AssertContains("same rejected commit", RecoveryPolicyEngine.Decide(facts, Policy()).First.Reason!);
                facts.ReworkSameCommit = false;
                facts.ReworkRepeatedCase = "case-7";
                AssertContains("same affected case (case-7)", RecoveryPolicyEngine.Decide(facts, Policy()).First.Reason!);
                facts.WorkerReadOnly = true;
                AssertEqual("Rework, Fail", Plan(facts, Policy()));
                facts.WorkerReadOnly = false;
                facts.MaxEscalations = 0;
                AssertContains("escalation is off (MaxEscalations 0)", RecoveryPolicyEngine.Decide(facts, Policy()).First.Reason!);
            }));

            cases.Add(Case("incident_rescues_waits_and_exhausts", "An incident waits for a rescue in flight, rescues within budget, then fails", TestTags.Positive, () =>
            {
                RecoveryFacts facts = new RecoveryFacts(RecoverySiteEnum.Incident) { RescueBudget = 2 };
                AssertEqual("Rescue", Plan(facts));
                facts.RescueInFlight = true;
                AssertEqual("Wait", Plan(facts));
                facts.RescueInFlight = false;
                facts.Rescues = 2;
                AssertEqual("Fail", Plan(facts));
                facts.Rescues = 0;
                facts.RescueLinksFull = true;
                AssertEqual("Fail", Plan(facts), "the link list is full");
                AssertEqual("Fail", Plan(new RecoveryFacts(RecoverySiteEnum.Incident)), "a zero budget never rescues");
            }));

            cases.Add(Case("null_facts_refused", "Decide refuses null facts", TestTags.Negative, () =>
            {
                bool threw = false;
                try { RecoveryPolicyEngine.Decide(null!); }
                catch (ArgumentNullException) { threw = true; }
                AssertTrue(threw);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.RecoveryPolicyEngine",
                displayName: "Recovery Policy Engine",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static RecoveryPolicy Policy()
        {
            return new RecoveryPolicy { MaxResumes = 3, UnclassifiedResumes = 1, EscalateAfterResumes = 3, JudgeReworkRounds = 2, OutputRepairAttempts = 1 };
        }

        private static RecoveryFacts Exit(RuntimeFailureKindEnum kind, int exitCode)
        {
            return new RecoveryFacts(RecoverySiteEnum.RuntimeExit) { RuntimeFailureKind = kind, ExitCode = exitCode };
        }

        private static string Plan(RecoveryFacts facts, RecoveryPolicy? policy = null)
        {
            return String.Join(", ", RecoveryPolicyEngine.Decide(facts, policy).Steps.Select(s => s.Action.ToString()));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.RecoveryPolicyEngine",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
