namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the centralized mission state machine: the legal transition matrix and the
    /// terminal / post-work / active status classifiers. Balanced coverage of allowed transitions
    /// (positive) and rejected ones (negative), including that terminal statuses permit no further
    /// transitions.
    /// </summary>
    public sealed class MissionStateMachineSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission State Machine suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ----- Legal transitions (positive) -----
            AddLegal(cases, MissionStatusEnum.Pending, MissionStatusEnum.Assigned);
            AddLegal(cases, MissionStatusEnum.Pending, MissionStatusEnum.Cancelled);
            AddLegal(cases, MissionStatusEnum.Assigned, MissionStatusEnum.InProgress);
            AddLegal(cases, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced);
            AddLegal(cases, MissionStatusEnum.InProgress, MissionStatusEnum.Review);
            AddLegal(cases, MissionStatusEnum.InProgress, MissionStatusEnum.Complete);
            AddLegal(cases, MissionStatusEnum.InProgress, MissionStatusEnum.Failed);
            AddLegal(cases, MissionStatusEnum.InProgress, MissionStatusEnum.Cancelled);
            AddLegal(cases, MissionStatusEnum.WorkProduced, MissionStatusEnum.PullRequestOpen);
            AddLegal(cases, MissionStatusEnum.WorkProduced, MissionStatusEnum.Complete);
            AddLegal(cases, MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed);
            AddLegal(cases, MissionStatusEnum.PullRequestOpen, MissionStatusEnum.Complete);
            AddLegal(cases, MissionStatusEnum.Review, MissionStatusEnum.Complete);
            AddLegal(cases, MissionStatusEnum.Review, MissionStatusEnum.InProgress);
            AddLegal(cases, MissionStatusEnum.LandingFailed, MissionStatusEnum.WorkProduced);

            // ----- Illegal transitions (negative) -----
            AddIllegal(cases, MissionStatusEnum.Pending, MissionStatusEnum.InProgress);   // must go via Assigned
            AddIllegal(cases, MissionStatusEnum.Pending, MissionStatusEnum.Complete);
            AddIllegal(cases, MissionStatusEnum.Assigned, MissionStatusEnum.WorkProduced);
            AddIllegal(cases, MissionStatusEnum.WorkProduced, MissionStatusEnum.InProgress);
            AddIllegal(cases, MissionStatusEnum.Review, MissionStatusEnum.WorkProduced);
            AddIllegal(cases, MissionStatusEnum.Complete, MissionStatusEnum.InProgress);
            AddIllegal(cases, MissionStatusEnum.Failed, MissionStatusEnum.InProgress);
            AddIllegal(cases, MissionStatusEnum.Cancelled, MissionStatusEnum.InProgress);
            AddIllegal(cases, MissionStatusEnum.Complete, MissionStatusEnum.Failed);

            // ----- Terminal statuses reject every transition (negative) -----
            cases.Add(Case("terminal_statuses_reject_all", "Terminal statuses permit no transition", TestTags.Negative, () =>
            {
                MissionStatusEnum[] terminals = { MissionStatusEnum.Complete, MissionStatusEnum.Failed, MissionStatusEnum.Cancelled };
                foreach (MissionStatusEnum from in terminals)
                {
                    foreach (MissionStatusEnum to in Enum.GetValues<MissionStatusEnum>())
                    {
                        AssertFalse(MissionStateMachine.IsValidTransition(from, to), from + "->" + to + " must be rejected");
                        AssertTrue(MissionStateMachine.IsTerminal(from), from + " is terminal");
                    }
                }
            }));

            // ----- Classifiers -----
            cases.Add(Case("classifiers_partition_statuses", "Terminal / post-work / active classifiers are correct", TestTags.Positive, () =>
            {
                AssertTrue(MissionStateMachine.IsTerminal(MissionStatusEnum.Complete));
                AssertFalse(MissionStateMachine.IsTerminal(MissionStatusEnum.InProgress));

                AssertTrue(MissionStateMachine.IsTerminalOrPostWork(MissionStatusEnum.WorkProduced));
                AssertTrue(MissionStateMachine.IsTerminalOrPostWork(MissionStatusEnum.LandingFailed));
                AssertTrue(MissionStateMachine.IsTerminalOrPostWork(MissionStatusEnum.Complete));
                AssertFalse(MissionStateMachine.IsTerminalOrPostWork(MissionStatusEnum.InProgress));

                AssertTrue(MissionStateMachine.IsActive(MissionStatusEnum.InProgress));
                AssertTrue(MissionStateMachine.IsActive(MissionStatusEnum.Review));
                AssertFalse(MissionStateMachine.IsActive(MissionStatusEnum.WorkProduced));
                AssertFalse(MissionStateMachine.IsActive(MissionStatusEnum.Complete));
            }));

            cases.Add(Case("agent_reportable_transitions_are_the_phase_toggle_only", "Agents may only toggle InProgress and Testing", TestTags.Negative, () =>
            {
                AssertTrue(MissionStateMachine.IsAgentReportableTransition(MissionStatusEnum.InProgress, MissionStatusEnum.Testing), "InProgress -> Testing");
                AssertTrue(MissionStateMachine.IsAgentReportableTransition(MissionStatusEnum.Testing, MissionStatusEnum.InProgress), "Testing -> InProgress");
                foreach (MissionStatusEnum from in Enum.GetValues<MissionStatusEnum>())
                {
                    foreach (MissionStatusEnum to in Enum.GetValues<MissionStatusEnum>())
                    {
                        bool toggle = (from == MissionStatusEnum.InProgress && to == MissionStatusEnum.Testing)
                            || (from == MissionStatusEnum.Testing && to == MissionStatusEnum.InProgress);
                        if (toggle) continue;
                        AssertFalse(MissionStateMachine.IsAgentReportableTransition(from, to), from + " -> " + to + " is not agent-reportable");
                    }
                }
            }));

            cases.Add(Case("mcp_transition_rules_match_state_machine", "MCP transition_mission_status uses the state machine for every status pair", TestTags.Positive, () =>
            {
                List<string> mismatches = new List<string>();
                foreach (MissionStatusEnum from in Enum.GetValues<MissionStatusEnum>())
                {
                    foreach (MissionStatusEnum to in Enum.GetValues<MissionStatusEnum>())
                    {
                        bool expected = MissionStateMachine.IsValidTransition(from, to);
                        bool actual = Armada.Server.Mcp.Tools.McpToolHelpers.IsValidTransition(from, to);
                        if (expected != actual) mismatches.Add(from + "->" + to + " (state machine " + expected + ", MCP " + actual + ")");
                    }
                }

                AssertTrue(mismatches.Count == 0, "MCP transition rules differ from MissionStateMachine: " + String.Join(", ", mismatches));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionStateMachine",
                displayName: "Mission State Machine",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void AddLegal(List<TestCaseDescriptor> cases, MissionStatusEnum from, MissionStatusEnum to)
        {
            cases.Add(Case("legal_" + from + "_to_" + to, from + " -> " + to + " is allowed", TestTags.Positive, () =>
            {
                AssertTrue(MissionStateMachine.IsValidTransition(from, to), from + " -> " + to + " should be legal");
            }));
        }

        private static void AddIllegal(List<TestCaseDescriptor> cases, MissionStatusEnum from, MissionStatusEnum to)
        {
            cases.Add(Case("illegal_" + from + "_to_" + to, from + " -> " + to + " is rejected", TestTags.Negative, () =>
            {
                AssertFalse(MissionStateMachine.IsValidTransition(from, to), from + " -> " + to + " should be illegal");
            }));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionStateMachine",
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
