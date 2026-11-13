namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Applies failure-cause rules to a stage outcome. Pure: the rules are data the administrator can edit
    /// (see <see cref="FailureCauseRuleCatalog"/> for the built-in defaults).
    /// </summary>
    public static class FailureCauseClassifier
    {
        #region Public-Members

        /// <summary>
        /// Longest a rule's text pattern may run against one failure reason.
        /// </summary>
        public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an outcome fell short and so needs a suspected cause: it did not complete, it recorded a failure, or
        /// its stage check failed or could not run. A Judge's own rejection of the work it reviewed is not its shortfall.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <returns>True when the outcome should be classified.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcome is null.</exception>
        public static bool IsShortfall(StageOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (outcome.Disposition == StageDispositionEnum.Pending) return false;
            // A Judge that rejected the work did its job: the rejection is a finding against the reviewed mission
            // (ReviewedMissionId), not a shortfall of the Judge.
            if (outcome.FailureKind == MissionFailureKindEnum.JudgeRejected
                && (String.Equals(outcome.JudgeVerdict, nameof(JudgeVerdictEnum.Fail), StringComparison.OrdinalIgnoreCase)
                    || String.Equals(outcome.JudgeVerdict, nameof(JudgeVerdictEnum.NeedsRevision), StringComparison.OrdinalIgnoreCase)))
                return false;
            if (outcome.Disposition != StageDispositionEnum.Completed) return true;
            if (outcome.FailureKind != null) return true;
            return outcome.StageCheckStatus == StageCheckStatusEnum.Failed || outcome.StageCheckStatus == StageCheckStatusEnum.CouldNotRun;
        }

        /// <summary>
        /// Classify an outcome: <see cref="FailureCauseEnum.None"/> when it did not fall short, else the cause of the first
        /// enabled rule (lowest Priority, then Name) that matches, else <see cref="FailureCauseEnum.Unknown"/>.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <param name="rules">Rules in any order; disabled rules are ignored.</param>
        /// <returns>The classification.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcome or rules is null.</exception>
        public static FailureCauseClassification Classify(StageOutcome outcome, IEnumerable<FailureCauseRule> rules)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            if (!IsShortfall(outcome))
                return new FailureCauseClassification { Cause = FailureCauseEnum.None, Source = FailureCauseSourceEnum.None };

            IEnumerable<FailureCauseRule> ordered = rules
                .Where(r => r != null && r.Enabled)
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (FailureCauseRule rule in ordered)
            {
                if (Matches(rule.Match, outcome))
                    return new FailureCauseClassification { Cause = rule.Cause, Source = FailureCauseSourceEnum.Rule, RuleId = rule.Id, Note = "Rule: " + rule.Name };
            }

            return new FailureCauseClassification { Cause = FailureCauseEnum.Unknown, Source = FailureCauseSourceEnum.None, Note = "No rule matched" };
        }

        /// <summary>
        /// Whether a rule's criteria match an outcome. Every criterion given must match; a match with no criteria matches
        /// nothing.
        /// </summary>
        /// <param name="match">Criteria.</param>
        /// <param name="outcome">Outcome.</param>
        /// <returns>True on a match.</returns>
        public static bool Matches(FailureCauseRuleMatch? match, StageOutcome? outcome)
        {
            if (match == null || outcome == null || !HasCriteria(match)) return false;

            if (match.Personas.Count > 0
                && !match.Personas.Any(p => String.Equals(p?.Trim(), outcome.Persona ?? PersonaCatalog.Worker, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (match.Runtimes.Count > 0 && (outcome.Runtime == null || !match.Runtimes.Contains(outcome.Runtime.Value))) return false;
            if (match.FailureKinds.Count > 0 && (outcome.FailureKind == null || !match.FailureKinds.Contains(outcome.FailureKind.Value))) return false;
            if (match.RuntimeFailureKinds.Count > 0 && (outcome.RuntimeFailureKind == null || !match.RuntimeFailureKinds.Contains(outcome.RuntimeFailureKind.Value))) return false;
            if (match.StageCheckStatuses.Count > 0 && (outcome.StageCheckStatus == null || !match.StageCheckStatuses.Contains(outcome.StageCheckStatus.Value))) return false;
            if (match.StageCheckReasons.Count > 0 && (outcome.StageCheckReason == null || !match.StageCheckReasons.Contains(outcome.StageCheckReason.Value))) return false;
            if (match.Dispositions.Count > 0 && !match.Dispositions.Contains(outcome.Disposition)) return false;

            if (!String.IsNullOrWhiteSpace(match.TextPattern))
            {
                if (String.IsNullOrEmpty(outcome.FailureReason)) return false;
                try
                {
                    if (!Regex.IsMatch(outcome.FailureReason!, match.TextPattern!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout)) return false;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a match has at least one criterion.
        /// </summary>
        /// <param name="match">Criteria.</param>
        /// <returns>True when at least one criterion is set.</returns>
        public static bool HasCriteria(FailureCauseRuleMatch? match)
        {
            if (match == null) return false;
            return match.Personas.Count > 0 || match.Runtimes.Count > 0 || match.FailureKinds.Count > 0
                || match.RuntimeFailureKinds.Count > 0 || match.StageCheckStatuses.Count > 0 || match.StageCheckReasons.Count > 0
                || match.Dispositions.Count > 0 || !String.IsNullOrWhiteSpace(match.TextPattern);
        }

        /// <summary>
        /// Check that a rule can be stored: a name, at least one criterion, a valid text pattern, and a cause other than
        /// <see cref="FailureCauseEnum.None"/>.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <returns>Null when valid, else what is wrong.</returns>
        /// <exception cref="CollectionLimitExceededException">Thrown when a Match list holds more than
        /// <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public static string? Validate(FailureCauseRule? rule)
        {
            if (rule == null) return "A rule is required.";
            if (String.IsNullOrWhiteSpace(rule.Name)) return "Name is required.";
            if (rule.Name.Trim().Length > 200) return "Name must be at most 200 characters.";
            if (rule.Match == null || !HasCriteria(rule.Match)) return "Match must set at least one criterion.";
            CollectionBounds.Validate(rule);
            if (rule.Cause == FailureCauseEnum.None) return "Cause must not be None.";
            if (!Enum.IsDefined(typeof(FailureCauseEnum), rule.Cause)) return "Cause is not a known value.";
            if (!String.IsNullOrWhiteSpace(rule.Match.TextPattern))
            {
                if (rule.Match.TextPattern!.Length > 1000) return "Match.TextPattern must be at most 1000 characters.";
                try
                {
                    _ = new Regex(rule.Match.TextPattern!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
                }
                catch (ArgumentException ex)
                {
                    return "Match.TextPattern is not a valid regular expression: " + ex.Message;
                }
            }

            return null;
        }

        #endregion
    }
}
