namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The alert rules over one scorecard group (docs/MODEL_HETEROGENEITY.md, Alerts, Decision 13). Pure functions: no
    /// database, no clock. A rule whose metric the scorecard does not provide is Unavailable (inactive) with the reason;
    /// a rule whose minimum count is not met is MinimumNotMet; nothing is invented.
    /// </summary>
    public static class ModelAlertRules
    {
        #region Public-Members

        /// <summary>Scorecard metric key of first-pass success.</summary>
        public const string FirstPassMetric = "firstPassSuccess";

        /// <summary>Scorecard metric key of escalations away in a row.</summary>
        public const string EscalationsInARowMetric = "escalations.inARow";

        /// <summary>Scorecard metric key of protocol repairs.</summary>
        public const string ProtocolRepairMetric = "repairs.protocol";

        /// <summary>Scorecard metric key of tokens per accepted result.</summary>
        public const string CostMetric = "cost.tokensPerAcceptedResult";

        /// <summary>Revision group name of attempts that match no recorded revision.</summary>
        public const string UnmatchedGroup = "unmatched";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Evaluate every rule for one scorecard group.
        /// </summary>
        /// <param name="group">Scorecard group.</param>
        /// <param name="thresholds">Effective thresholds of the persona (every value set).</param>
        /// <param name="otherCosts">Tokens per accepted result of the other profiles' groups in the same persona and vessel
        /// scope that have at least the cost rule's minimum accepted results.</param>
        /// <returns>One result per rule, in rule order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static List<ModelAlertRuleResult> Evaluate(ModelScorecardGroup group, ModelAlertThreshold thresholds, IReadOnlyList<double> otherCosts)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (thresholds == null) throw new ArgumentNullException(nameof(thresholds));
            if (otherCosts == null) throw new ArgumentNullException(nameof(otherCosts));
            ModelAlertThreshold builtIn = ModelAlertThreshold.BuiltInDefaults();
            List<ModelAlertRuleResult> results = new List<ModelAlertRuleResult>();

            results.Add(RateRule(ModelAlertRuleEnum.FirstPassSuccess, Find(group, FirstPassMetric),
                thresholds.FirstPassMinRate ?? builtIn.FirstPassMinRate!.Value, thresholds.FirstPassMinMissions ?? builtIn.FirstPassMinMissions!.Value, below: true, "missions"));

            ModelAlertRuleResult streak = new ModelAlertRuleResult { Rule = ModelAlertRuleEnum.EscalationsInARow };
            int needed = thresholds.EscalationsInARow ?? builtIn.EscalationsInARow!.Value;
            streak.Threshold = needed;
            streak.Minimum = needed;
            ModelScorecardMetric? streakMetric = Find(group, EscalationsInARowMetric);
            if (!IsUsable(streakMetric, streak))
            {
                results.Add(streak);
            }
            else
            {
                streak.Count = streakMetric!.Count;
                streak.Of = streakMetric.Of;
                streak.Value = streakMetric.Count;
                streak.State = streakMetric.Count!.Value >= needed ? ModelAlertRuleStateEnum.Crossed : ModelAlertRuleStateEnum.Clear;
                results.Add(streak);
            }

            results.Add(RateRule(ModelAlertRuleEnum.ProtocolRepairRate, Find(group, ProtocolRepairMetric),
                thresholds.ProtocolRepairMaxRate ?? builtIn.ProtocolRepairMaxRate!.Value, thresholds.ProtocolRepairMinAttempts ?? builtIn.ProtocolRepairMinAttempts!.Value, below: false, "attempts"));

            results.Add(CostRule(Find(group, CostMetric), thresholds.CostMedianMultiplier ?? builtIn.CostMedianMultiplier!.Value,
                thresholds.CostMinAccepted ?? builtIn.CostMinAccepted!.Value, otherCosts));
            return results;
        }

        /// <summary>
        /// Tokens per accepted result of a group when it has at least the minimum accepted results with recorded usage,
        /// else null (used to build the other side of the cost comparison).
        /// </summary>
        /// <param name="group">Scorecard group.</param>
        /// <param name="minAccepted">Minimum accepted results.</param>
        /// <returns>The value, or null.</returns>
        public static double? QualifyingCost(ModelScorecardGroup group, int minAccepted)
        {
            if (group == null) return null;
            ModelScorecardMetric? metric = Find(group, CostMetric);
            if (metric == null || !metric.Available || !metric.Value.HasValue || (metric.Count ?? 0) < minAccepted) return null;
            return metric.Value.Value;
        }

        /// <summary>
        /// Median of a list of values (the mean of the middle two for an even count), or null when empty.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>The median, or null.</returns>
        public static double? Median(IEnumerable<double> values)
        {
            if (values == null) return null;
            List<double> sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return null;
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        /// <summary>
        /// Revision group name of a scorecard group: its revision identifiers comma separated, or "unmatched".
        /// </summary>
        /// <param name="group">Scorecard group.</param>
        /// <returns>The name.</returns>
        public static string GroupName(ModelScorecardGroup group)
        {
            if (group == null || group.RevisionIds.Count == 0) return UnmatchedGroup;
            return String.Join(",", group.RevisionIds);
        }

        /// <summary>
        /// The dedupe key of an alert condition: SHA-256 of tenant, profile, revision group, persona, vessel scope and rule.
        /// </summary>
        /// <param name="tenantId">Tenant, or null.</param>
        /// <param name="profileId">Profile.</param>
        /// <param name="revisionGroup">Revision group.</param>
        /// <param name="persona">Persona (normalized case-insensitively).</param>
        /// <param name="vesselId">Vessel, or null for every vessel.</param>
        /// <param name="rule">Rule.</param>
        /// <returns>Lowercase hex key.</returns>
        public static string Key(string? tenantId, string profileId, string revisionGroup, string persona, string? vesselId, ModelAlertRuleEnum rule)
        {
            string raw = (tenantId ?? "") + "\n" + profileId + "\n" + revisionGroup + "\n" + PersonaCatalog.NormalizeName(persona).ToLowerInvariant()
                + "\n" + (String.IsNullOrEmpty(vesselId) ? "*" : vesselId) + "\n" + rule.ToString();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// The alert title in the style of the design doc, with counts, for example
        /// "qwen3-coder:30b as Worker on Armada: first pass 4 of 11 (36%, 95% CI 15-65%)".
        /// </summary>
        /// <param name="target">Evaluated target.</param>
        /// <param name="result">Crossed rule result.</param>
        /// <returns>The title.</returns>
        public static string Title(ModelAlertTarget target, ModelAlertRuleResult result)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (result == null) throw new ArgumentNullException(nameof(result));
            string subject = (String.IsNullOrWhiteSpace(target.Model) ? target.ProfileId : target.Model!) + " as " + target.Persona
                + " on " + (String.IsNullOrEmpty(target.VesselId) ? "all vessels" : (target.VesselName ?? target.VesselId!));
            switch (result.Rule)
            {
                case ModelAlertRuleEnum.FirstPassSuccess:
                    return subject + ": first pass " + result.Count + " of " + result.Of + RateText(result);
                case ModelAlertRuleEnum.EscalationsInARow:
                    return subject + ": " + result.Count + " escalations away in a row";
                case ModelAlertRuleEnum.ProtocolRepairRate:
                    return subject + ": protocol repairs " + result.Count + " of " + result.Of + " attempts" + RateText(result);
                default:
                    return subject + ": " + Tokens(result.Value) + " tokens per accepted result over " + result.Count + " accepted results";
            }
        }

        /// <summary>
        /// The alert detail: the threshold, the comparison and what the alert suggests.
        /// </summary>
        /// <param name="target">Evaluated target.</param>
        /// <param name="result">Crossed rule result.</param>
        /// <returns>The detail.</returns>
        public static string Detail(ModelAlertTarget target, ModelAlertRuleResult result)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (result == null) throw new ArgumentNullException(nameof(result));
            string revisions = "Revision group " + target.RevisionGroup + "; " + target.Missions + " missions, " + target.Attempts + " attempts in the scorecard window.";
            switch (result.Rule)
            {
                case ModelAlertRuleEnum.FirstPassSuccess:
                    return "First-pass success is below " + Percent(result.Threshold) + " over at least " + result.Minimum + " missions. "
                        + revisions + " Consider moving it down in the " + target.Persona + " priorities, or re-evaluate it.";
                case ModelAlertRuleEnum.EscalationsInARow:
                    return "The last " + result.Count + " attempts of " + result.Of + " were escalated away to another captain (threshold "
                        + result.Minimum + " in a row). " + revisions + " Consider moving it down in the " + target.Persona + " priorities.";
                case ModelAlertRuleEnum.ProtocolRepairRate:
                    return "The protocol repair rate is above " + Percent(result.Threshold) + " over at least " + result.Minimum + " attempts. "
                        + revisions + " The work may be fine and the report wrong: the suggested fix is the compact template, not a different model.";
                default:
                    return "Tokens per accepted result are above " + Tokens(result.Threshold) + " (the multiple of the " + target.Persona
                        + " median of " + Tokens(result.Median) + " across " + result.MedianOf + " other profile groups with at least "
                        + result.Minimum + " accepted results). " + revisions;
            }
        }

        #endregion

        #region Private-Methods

        private static ModelAlertRuleResult RateRule(ModelAlertRuleEnum rule, ModelScorecardMetric? metric, double threshold, int minimum, bool below, string unit)
        {
            ModelAlertRuleResult result = new ModelAlertRuleResult { Rule = rule, Threshold = threshold, Minimum = minimum };
            if (!IsUsable(metric, result)) return result;
            result.Count = metric!.Count;
            result.Of = metric.Of;
            result.Value = metric.Rate;
            result.Lower = metric.Lower;
            result.Upper = metric.Upper;
            int of = metric.Of ?? 0;
            if (of < minimum)
            {
                result.State = ModelAlertRuleStateEnum.MinimumNotMet;
                result.Reason = of + " " + unit + " counted; the rule needs at least " + minimum + ".";
                return result;
            }
            double rate = (double)(metric.Count ?? 0) / of;
            bool crossed = below ? rate < threshold : rate > threshold;
            result.State = crossed ? ModelAlertRuleStateEnum.Crossed : ModelAlertRuleStateEnum.Clear;
            return result;
        }

        private static ModelAlertRuleResult CostRule(ModelScorecardMetric? metric, double multiplier, int minimum, IReadOnlyList<double> otherCosts)
        {
            ModelAlertRuleResult result = new ModelAlertRuleResult { Rule = ModelAlertRuleEnum.CostPerAcceptedResult, Minimum = minimum };
            if (metric == null)
            {
                result.State = ModelAlertRuleStateEnum.Unavailable;
                result.Reason = "The scorecard does not report " + CostMetric + ".";
                return result;
            }
            result.Count = metric.Count;
            result.Of = metric.Of;
            result.Value = metric.Value;
            if (!metric.Available || !metric.Value.HasValue)
            {
                bool tooFew = (metric.Of ?? 0) < minimum;
                result.State = tooFew ? ModelAlertRuleStateEnum.MinimumNotMet : ModelAlertRuleStateEnum.Unavailable;
                result.Reason = metric.UnavailableReason ?? "Tokens per accepted result are not available.";
                return result;
            }
            if ((metric.Count ?? 0) < minimum)
            {
                result.State = ModelAlertRuleStateEnum.MinimumNotMet;
                result.Reason = (metric.Count ?? 0) + " accepted results with recorded usage; the rule needs at least " + minimum + ".";
                return result;
            }
            double? median = Median(otherCosts);
            result.MedianOf = otherCosts.Count;
            if (!median.HasValue)
            {
                result.State = ModelAlertRuleStateEnum.MinimumNotMet;
                result.Reason = "No other profile in this persona has at least " + minimum + " accepted results to compare with.";
                return result;
            }
            result.Median = Math.Round(median.Value, 1);
            result.Threshold = Math.Round(median.Value * multiplier, 1);
            result.State = metric.Value.Value > median.Value * multiplier ? ModelAlertRuleStateEnum.Crossed : ModelAlertRuleStateEnum.Clear;
            return result;
        }

        private static bool IsUsable(ModelScorecardMetric? metric, ModelAlertRuleResult result)
        {
            if (metric == null)
            {
                result.State = ModelAlertRuleStateEnum.Unavailable;
                result.Reason = "The scorecard does not report this metric.";
                return false;
            }
            if (!metric.Available || !metric.Count.HasValue)
            {
                result.State = ModelAlertRuleStateEnum.Unavailable;
                result.Reason = metric.UnavailableReason ?? "The scorecard does not report this metric.";
                return false;
            }
            return true;
        }

        private static ModelScorecardMetric? Find(ModelScorecardGroup group, string key)
        {
            return group.Metrics.FirstOrDefault(m => String.Equals(m.Key, key, StringComparison.Ordinal));
        }

        private static string RateText(ModelAlertRuleResult result)
        {
            if (!result.Value.HasValue) return "";
            string text = " (" + Percent(result.Value);
            if (result.Lower.HasValue && result.Upper.HasValue)
                text += ", 95% CI " + Math.Round(result.Lower.Value * 100).ToString(CultureInfo.InvariantCulture) + "-" + Percent(result.Upper);
            return text + ")";
        }

        private static string Percent(double? value)
        {
            if (!value.HasValue) return "?";
            return Math.Round(value.Value * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Tokens(double? value)
        {
            if (!value.HasValue) return "?";
            return Math.Round(value.Value).ToString("0", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
