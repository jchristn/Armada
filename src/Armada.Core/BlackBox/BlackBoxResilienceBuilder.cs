namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the resilience section (analysis.resilience) from the rendered rules: the strongest in-scope rule's
    /// self-healing verdict, detection gap and invariant; code pointers from the failure site, the rules and the phase's
    /// handoff and landing services; proposed Armada changes as mission briefs ranked by impact, their severity escalated
    /// by recurrence; the regression test; and the automatic recoveries Armada already attempted. In scope it is never
    /// empty (the phase defaults fill what no rule covers); outside the scope it says why no Armada change is suggested
    /// unless a rule or improvement suggests one. Internal to <see cref="BlackBoxAdvisor"/>; pure and thread-safe.
    /// </summary>
    internal static class BlackBoxResilienceBuilder
    {
        #region Public-Methods

        public static BlackBoxResilience? Render(BlackBoxRule rule, BlackBoxRecord record, ICollection<string>? diagnostics)
        {
            BlackBoxRuleResilience? t = rule.Resilience;
            if (t == null) return null;
            return RenderTemplate(t, rule.Id, record, diagnostics);
        }

        public static BlackBoxResilience Build(BlackBoxRecord record, List<BlackBoxRuleOutput> ordered, BlackBoxTriageVerdict? triage, bool inScope, string? scopeReason, List<BlackBoxImprovement> improvements, ICollection<string>? diagnostics)
        {
            BlackBoxResilience res = new BlackBoxResilience();
            res.InScope = inScope;
            res.ScopeReason = inScope ? scopeReason : null;
            res.ArmadaVersion = record.Site?.ArmadaVersion;
            res.ArmadaCommit = record.Site?.ArmadaCommit;
            res.Occurrences30Days = Math.Max(1, record.Related?.OccurrencesLast30Days ?? 0);
            res.ArmadaVersionCount = Math.Max(String.IsNullOrEmpty(record.Site?.ArmadaVersion) ? 0 : 1, record.Related?.ArmadaVersions.Count ?? 0);
            res.AttemptedRecoveries = Attempted(record);

            List<BlackBoxResilience> parts = new List<BlackBoxResilience>();
            List<string> ruleIds = new List<string>();
            foreach (BlackBoxRuleOutput o in ordered)
            {
                if (o.Resilience == null) continue;
                parts.Add(o.Resilience);
                ruleIds.Add(o.Rule.Id);
            }
            if (inScope && parts.Count == 0)
            {
                // No matched rule covers this in-scope failure: the phase defaults keep the section from being empty.
                BlackBoxPhaseEnum phase = BlackBoxRuleFacts.Phase(record);
                BlackBoxResilience fallback = RenderTemplate(BlackBoxResilienceDefaults.ForStep(phase, BlackBoxRuleFacts.Step(record)), null, record, diagnostics);
                parts.Add(fallback);
            }
            if (inScope && triage != null && triage.PriorApplied) ruleIds.Add(BlackBoxResilienceScope.PriorRuleId);
            res.RuleIds = ruleIds.Distinct(StringComparer.Ordinal).Take(BlackBoxLimits.MaxResilienceRules).ToList();

            BlackBoxResilience? lead = parts.FirstOrDefault(p => p.SelfHealable != BlackBoxSelfHealableEnum.Unknown || p.SelfHealingProposal != null) ?? parts.FirstOrDefault();
            if (lead != null)
            {
                res.SelfHealable = lead.SelfHealable;
                res.SelfHealingProposal = lead.SelfHealingProposal;
            }
            BlackBoxResilience? gap = parts.FirstOrDefault(p => p.DetectionGap != null);
            if (gap != null)
            {
                res.DetectionGapKind = gap.DetectionGapKind;
                res.DetectionGap = gap.DetectionGap;
            }
            res.Invariant = parts.Select(p => p.Invariant).FirstOrDefault(i => i != null);
            res.RegressionTest = parts.Select(p => p.RegressionTest).FirstOrDefault(x => x != null);

            List<string> pointers = new List<string>();
            string? site = SitePointer(record);
            if (site != null) pointers.Add(site);
            foreach (BlackBoxResilience p in parts) pointers.AddRange(p.CodePointers);
            foreach (BlackBoxImprovement imp in improvements) pointers.AddRange(imp.SourcePointers);
            if (inScope) pointers.AddRange(BlackBoxResilienceDefaults.PointersFor(BlackBoxRuleFacts.Phase(record)));
            res.CodePointers = pointers.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(BlackBoxLimits.MaxCodePointers).ToList();

            List<BlackBoxProposedChange> changes = new List<BlackBoxProposedChange>();
            foreach (BlackBoxResilience p in parts) changes.AddRange(p.ProposedChanges);
            if (changes.Count == 0)
            {
                // Outside the scope (or a scope rule without changes) the improvement hypotheses become the proposals.
                foreach (BlackBoxImprovement imp in improvements)
                {
                    changes.Add(new BlackBoxProposedChange
                    {
                        Title = (imp.Area != null ? imp.Area + ": " : String.Empty) + FirstSentence(imp.Hypothesis),
                        Change = imp.Hypothesis + (imp.Evidence != null ? " Evidence: " + imp.Evidence : String.Empty),
                        AcceptanceTest = null,
                        Severity = imp.Severity,
                        RuleId = imp.RuleId,
                        CodePointers = imp.SourcePointers.Take(BlackBoxLimits.MaxSourcePointers).ToList()
                    });
                }
            }
            List<BlackBoxProposedChange> ranked = changes
                .GroupBy(c => c.Title, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Select((c, i) => new { c, i })
                .OrderByDescending(x => BlackBoxResilienceSeverity.Order(x.c.Severity)).ThenBy(x => x.i)
                .Select(x => x.c).Take(BlackBoxLimits.MaxProposedChanges).ToList();
            int rank = 1;
            BlackBoxSeverityEnum overall = BlackBoxSeverityEnum.Low;
            foreach (BlackBoxProposedChange c in ranked)
            {
                c.Rank = rank++;
                c.Severity = BlackBoxResilienceSeverity.Escalate(c.Severity, res.Occurrences30Days, res.ArmadaVersionCount);
                if (c.CodePointers.Count == 0) c.CodePointers = res.CodePointers.Take(BlackBoxLimits.MaxSourcePointers).ToList();
                c.MissionBrief = MissionBrief(record, res, c);
                overall = BlackBoxResilienceSeverity.Max(overall, c.Severity);
            }
            res.ProposedChanges = ranked;
            res.Severity = ranked.Count > 0 ? overall : BlackBoxSeverityEnum.Low;

            if (!inScope && ranked.Count == 0)
            {
                string verdict = triage == null || triage.Verdict == BlackBoxTriageEnum.Unknown ? "undecided" : triage.Verdict.ToString();
                res.NoChangeReason = "No Armada change suggested: the failure is outside Armada's handoff and landing (phase " + BlackBoxRuleFacts.Phase(record) + ", step " + BlackBoxRuleFacts.Step(record) + ") and the triage is " + verdict + ".";
                res.SelfHealable = res.SelfHealable == BlackBoxSelfHealableEnum.Unknown && triage != null && (triage.Verdict == BlackBoxTriageEnum.UserConfig || triage.Verdict == BlackBoxTriageEnum.MissionBrief || triage.Verdict == BlackBoxTriageEnum.External)
                    ? BlackBoxSelfHealableEnum.No : res.SelfHealable;
                res.DetectionGapKind = res.DetectionGap == null ? BlackBoxDetectionGapEnum.None : res.DetectionGapKind;
            }
            return res;
        }

        #endregion

        #region Private-Methods

        private static BlackBoxResilience RenderTemplate(BlackBoxRuleResilience t, string? ruleId, BlackBoxRecord record, ICollection<string>? diagnostics)
        {
            string label = ruleId ?? "scope default";
            BlackBoxResilience r = new BlackBoxResilience();
            r.SelfHealable = t.SelfHealable;
            r.SelfHealingProposal = Text(t.SelfHealing, record, label, "self-healing", diagnostics);
            r.DetectionGapKind = t.GapKind;
            r.DetectionGap = Text(t.DetectionGap, record, label, "detection gap", diagnostics);
            r.Invariant = Text(t.Invariant, record, label, "invariant", diagnostics);
            r.CodePointers = t.CodePointers.Take(BlackBoxLimits.MaxCodePointers).ToList();
            string? test = Text(t.RegressionTest, record, label, "regression test", diagnostics);
            if (test != null) r.RegressionTest = new BlackBoxRegressionTest { Suite = t.RegressionSuite ?? BlackBoxResilienceDefaults.SuiteFor(BlackBoxRuleFacts.Phase(record)), Description = test };
            foreach (BlackBoxRuleChange c in t.Changes.Take(BlackBoxLimits.MaxProposedChanges))
            {
                string? title = Text(c.Title, record, label, "change title", diagnostics);
                string? change = Text(c.Change, record, label, "change", diagnostics);
                if (title == null || change == null) continue;
                r.ProposedChanges.Add(new BlackBoxProposedChange
                {
                    Title = title,
                    Change = change,
                    AcceptanceTest = Text(c.AcceptanceTest, record, label, "acceptance test", diagnostics),
                    Severity = c.Severity,
                    RuleId = ruleId,
                    CodePointers = c.CodePointers.Take(BlackBoxLimits.MaxSourcePointers).ToList()
                });
            }
            return r;
        }

        private static string? Text(string? template, BlackBoxRecord record, string label, string part, ICollection<string>? diagnostics)
        {
            if (template == null) return null;
            List<string> missing = new List<string>();
            string? text = BlackBoxRuleTemplate.Render(template, record, missing);
            if (text == null) diagnostics?.Add("rule " + label + ": resilience " + part + " left out (missing " + String.Join(", ", missing.Distinct()) + ")");
            return text;
        }

        private static string? SitePointer(BlackBoxRecord record)
        {
            BlackBoxSite? site = record.Site;
            if (site == null || String.IsNullOrWhiteSpace(site.SourceFile)) return null;
            return site.SourceFile + (String.IsNullOrWhiteSpace(site.Method) ? String.Empty : ":" + site.Method);
        }

        private static List<string> Attempted(BlackBoxRecord record)
        {
            List<string> list = new List<string>();
            BlackBoxHandoffTrail? trail = record.Handoff;
            if (trail != null)
            {
                foreach (BlackBoxAutoRecovery a in trail.AutoRecoveries)
                {
                    list.Add(a.Kind + " x" + a.Attempts.ToString(CultureInfo.InvariantCulture) + " (" + a.Outcome + ")"
                        + (a.LastUtc.HasValue ? ", last " + a.LastUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z" : String.Empty)
                        + (a.Detail != null ? ": " + OneLine(BlackBoxLimits.Clip(a.Detail, 200)!) : String.Empty));
                }
            }
            BlackBoxSubject? s = record.Subject;
            if (s != null)
            {
                int redispatches = s.RedispatchAttempts ?? 0;
                int repairs = s.OutputRepairAttempts ?? 0;
                int rescues = s.RescueChain?.Count ?? 0;
                if (redispatches > 0 && !list.Any(l => l.StartsWith("Redispatch ", StringComparison.Ordinal))) list.Add("Redispatch x" + redispatches.ToString(CultureInfo.InvariantCulture) + " (mission counter)");
                if (repairs > 0 && !list.Any(l => l.StartsWith("OutputRepair ", StringComparison.Ordinal))) list.Add("OutputRepair x" + repairs.ToString(CultureInfo.InvariantCulture) + " (mission counter)");
                if (rescues > 0 && !list.Any(l => l.StartsWith("Rescue ", StringComparison.Ordinal))) list.Add("Rescue x" + rescues.ToString(CultureInfo.InvariantCulture) + " (rescue chain)");
            }
            return list.Take(BlackBoxLimits.MaxAttemptedRecoveries).ToList();
        }

        private static string MissionBrief(BlackBoxRecord record, BlackBoxResilience res, BlackBoxProposedChange c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(c.Title).Append("\n\n");
            sb.Append(c.Change).Append("\n\n");
            if (c.AcceptanceTest != null) sb.Append("Acceptance test: ").Append(c.AcceptanceTest).Append("\n\n");
            if (c.CodePointers.Count > 0)
            {
                sb.Append("Code pointers");
                if (res.ArmadaVersion != null || res.ArmadaCommit != null)
                    sb.Append(" (Armada ").Append(res.ArmadaVersion ?? "unknown").Append(res.ArmadaCommit != null ? ", commit " + res.ArmadaCommit : String.Empty).Append(')');
                sb.Append(": ").Append(String.Join(", ", c.CodePointers)).Append("\n\n");
            }
            sb.Append("Evidence: black box entry ").Append(String.IsNullOrEmpty(record.EntryId) ? "?" : record.EntryId)
                .Append(" (").Append(BlackBoxRuleFacts.Phase(record)).Append(" / ").Append(BlackBoxRuleFacts.Step(record));
            if (c.RuleId != null) sb.Append(", rule ").Append(c.RuleId);
            sb.Append("), seen ").Append(res.Occurrences30Days.ToString(CultureInfo.InvariantCulture)).Append(res.Occurrences30Days == 1 ? " time" : " times").Append(" in 30 days");
            if (res.ArmadaVersionCount > 1) sb.Append(" across ").Append(res.ArmadaVersionCount.ToString(CultureInfo.InvariantCulture)).Append(" Armada versions");
            sb.Append(". Severity ").Append(c.Severity).Append('.');
            if (res.DetectionGap != null) sb.Append(" Detection gap: ").Append(res.DetectionGap);
            return BlackBoxLimits.Clip(sb.ToString().Replace('—', '-').Replace('–', '-'), BlackBoxLimits.MaxShortTextChars) ?? String.Empty;
        }

        private static string FirstSentence(string text)
        {
            string t = OneLine(text).Trim();
            int dot = t.IndexOf(". ", StringComparison.Ordinal);
            string first = dot > 0 ? t.Substring(0, dot) : t.TrimEnd('.');
            return first.Length > 120 ? first.Substring(0, 117).TrimEnd() + "..." : first;
        }

        private static string OneLine(string text)
        {
            return text.Replace("\r", " ").Replace("\n", " ");
        }

        #endregion
    }
}
