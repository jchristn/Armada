namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The failure-cause rules Armada ships. They are stored on first start (for every tenant, TenantId null) and can
    /// then be edited or disabled like any rule, but not deleted. A built-in rule is identified by its Name, so a rule
    /// the administrator edited is never replaced.
    /// </summary>
    public static class FailureCauseRuleCatalog
    {
        #region Public-Methods

        /// <summary>
        /// Build the built-in rules, in evaluation order.
        /// </summary>
        /// <returns>New rule instances (IsBuiltIn true, TenantId null).</returns>
        public static List<FailureCauseRule> Build()
        {
            List<FailureCauseRule> rules = new List<FailureCauseRule>();

            rules.Add(Rule(10, "Infrastructure, operator, or dependency",
                "Failures Armada or a person caused, or that came from another mission: excluded from the model's results.",
                FailureCauseEnum.NotTheModel,
                m => m.FailureKinds.AddRange(new[] { MissionFailureKindEnum.Infra, MissionFailureKindEnum.OperatorAction, MissionFailureKindEnum.DependencyFailed, MissionFailureKindEnum.LandingConflict, MissionFailureKindEnum.ReviewDenied })));

            rules.Add(Rule(15, "Captain reported blocked",
                "The captain reported BLOCKED (an external prerequisite or a contradictory brief) and left no work: excluded from the model's results.",
                FailureCauseEnum.NotTheModel,
                m => m.FailureKinds.Add(MissionFailureKindEnum.Blocked)));

            rules.Add(Rule(20, "Runtime or account unavailable",
                "The runtime could not serve the model (usage limit, authentication, model not available).",
                FailureCauseEnum.NotTheModel,
                m => m.RuntimeFailureKinds.AddRange(new[] { RuntimeFailureKindEnum.UsageLimit, RuntimeFailureKindEnum.AuthFailure, RuntimeFailureKindEnum.ModelUnavailable })));

            rules.Add(Rule(30, "Interrupted or cancelled",
                "The agent was stopped from outside (a restart, a dropped Harbor, a cancel).",
                FailureCauseEnum.NotTheModel,
                m => m.Dispositions.AddRange(new[] { StageDispositionEnum.Interrupted, StageDispositionEnum.Cancelled })));

            rules.Add(Rule(40, "Stage check host unavailable",
                "The stage check could not reach the dock's Harbor.",
                FailureCauseEnum.NotTheModel,
                m => m.StageCheckReasons.Add(StageCheckReasonEnum.HarborDisconnected)));

            rules.Add(Rule(50, "Malformed tool call",
                "The model emitted a tool call the runtime could not parse.",
                FailureCauseEnum.ToolUse,
                m => m.TextPattern = @"(error parsing tool call|malformed tool call|invalid tool call|tool call.{0,40}(parse|invalid))"));

            rules.Add(Rule(60, "Context exhausted",
                "The conversation outgrew the model's context window.",
                FailureCauseEnum.Context,
                m => m.TextPattern = @"(context (length|window) (exceeded|is too)|maximum context length|context_length_exceeded|prompt is too long)"));

            rules.Add(Rule(70, "Stalled or out of iterations",
                "The tool loop stalled or ran out of iterations without finishing.",
                FailureCauseEnum.ToolUse,
                m => m.FailureKinds.Add(MissionFailureKindEnum.StallRecoveryExhausted)));

            rules.Add(Rule(80, "Output contract",
                "The report broke the output contract (markers, sections, plan block), or had to be repaired.",
                FailureCauseEnum.Protocol,
                m => m.FailureKinds.Add(MissionFailureKindEnum.InvalidOutput)));

            rules.Add(Rule(81, "Output repair scheduled",
                "The stage was rerun only to repair its output contract.",
                FailureCauseEnum.Protocol,
                m => m.Dispositions.Add(StageDispositionEnum.OutputRepair)));

            rules.Add(Rule(90, "Scope or boundary",
                "The stage changed files or areas outside its mission.",
                FailureCauseEnum.InstructionFollowing,
                m => m.FailureKinds.AddRange(new[] { MissionFailureKindEnum.ScopeViolation, MissionFailureKindEnum.Boundary })));

            rules.Add(Rule(100, "No changes produced",
                "The stage reported success without producing the requested change.",
                FailureCauseEnum.InstructionFollowing,
                m => m.FailureKinds.Add(MissionFailureKindEnum.NoOp)));

            rules.Add(Rule(110, "Build or tests failed",
                "The stage's work did not build or pass its tests.",
                FailureCauseEnum.Capability,
                m => m.FailureKinds.AddRange(new[] { MissionFailureKindEnum.Compile, MissionFailureKindEnum.TestFail })));

            rules.Add(Rule(111, "Stage check failed",
                "The vessel's build or unit tests failed after the stage.",
                FailureCauseEnum.Capability,
                m => m.StageCheckStatuses.Add(StageCheckStatusEnum.Failed)));

            return rules;
        }

        #endregion

        #region Private-Methods

        private static FailureCauseRule Rule(int priority, string name, string description, FailureCauseEnum cause, Action<FailureCauseRuleMatch> configure)
        {
            FailureCauseRule rule = new FailureCauseRule
            {
                TenantId = null,
                Name = name,
                Description = description,
                Priority = priority,
                Enabled = true,
                IsBuiltIn = true,
                Cause = cause
            };
            configure(rule.Match);
            return rule;
        }

        #endregion
    }
}
