namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// The black box advisor: runs a <see cref="BlackBoxRuleCatalog"/> over a record and returns why findings (with
    /// confidence and evidence references), configuration recommendations, Armada improvement hypotheses, ranked recovery
    /// paths with machine-readable actions, a weighted triage verdict (a tie reports Unknown with every tied candidate)
    /// and a plain-language summary assembled from the findings. Pure and deterministic: no I/O, the same record and
    /// catalog always give the same analysis. Rules whose text cannot be filled from the record are skipped and logged.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxAdvisor : IBlackBoxAdvisor
    {
        #region Public-Members

        /// <inheritdoc />
        public int RulesVersion => _Catalog.RulesVersion;

        /// <summary>
        /// The catalog this advisor runs.
        /// </summary>
        public BlackBoxRuleCatalog Catalog => _Catalog;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxAdvisor] ";
        private readonly BlackBoxRuleCatalog _Catalog;
        private readonly LoggingModule? _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="catalog">Catalog, or null for <see cref="BlackBoxRuleCatalog.Default"/>.</param>
        /// <param name="logging">Logging module for skipped rules, or null.</param>
        public BlackBoxAdvisor(BlackBoxRuleCatalog? catalog = null, LoggingModule? logging = null)
        {
            _Catalog = catalog ?? BlackBoxRuleCatalog.Default;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public BlackBoxAnalysis? Analyze(BlackBoxRecord record)
        {
            if (record == null) return null;
            List<string> diagnostics = new List<string>();
            BlackBoxAnalysis analysis = Analyze(record, _Catalog, diagnostics);
            if (_Logging != null)
            {
                foreach (string d in diagnostics) _Logging.Debug(_Header + (record.EntryId ?? "?") + ": " + d);
            }
            return analysis;
        }

        /// <summary>
        /// Analyze a record with a catalog.
        /// </summary>
        /// <param name="record">Record (not modified).</param>
        /// <param name="catalog">Catalog.</param>
        /// <param name="diagnostics">Receives one line per skipped rule or component (may be null).</param>
        /// <returns>The analysis (never null).</returns>
        /// <exception cref="ArgumentNullException">Thrown when record or catalog is null.</exception>
        public static BlackBoxAnalysis Analyze(BlackBoxRecord record, BlackBoxRuleCatalog catalog, ICollection<string>? diagnostics = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            List<BlackBoxRule> matched = new List<BlackBoxRule>();
            foreach (BlackBoxRule rule in catalog.Rules.Where(r => !r.IsFallback))
            {
                if (SafeMatch(rule, record, diagnostics)) matched.Add(rule);
            }
            // Render first, then let generic rules step aside for the specific rules that produced output (a specific
            // rule skipped for a missing placeholder does not silence the generic one).
            List<BlackBoxRuleOutput> rendered = new List<BlackBoxRuleOutput>();
            foreach (BlackBoxRule rule in matched)
            {
                BlackBoxRuleOutput? output = Render(rule, record, diagnostics);
                if (output != null) rendered.Add(output);
            }
            HashSet<string> renderedIds = new HashSet<string>(rendered.Select(o => o.Rule.Id), StringComparer.Ordinal);
            List<BlackBoxRuleOutput> outputs = rendered.Where(o => !o.Rule.SuppressedBy.Any(renderedIds.Contains)).ToList();
            if (outputs.Count == 0)
            {
                foreach (BlackBoxRule fallback in catalog.Rules.Where(r => r.IsFallback))
                {
                    BlackBoxRuleOutput? output = Render(fallback, record, diagnostics);
                    if (output != null) { outputs.Add(output); break; }
                }
            }

            // Strongest votes first, catalog order breaks ties (stable sort).
            List<BlackBoxRuleOutput> ordered = outputs.Select((o, i) => new { o, i }).OrderByDescending(x => x.o.Rule.Weight).ThenBy(x => x.i).Select(x => x.o).ToList();

            BlackBoxAnalysis analysis = new BlackBoxAnalysis();
            analysis.Why = ordered.Select(o => o.Finding).Take(BlackBoxLimits.MaxFindings).ToList();
            analysis.ConfigRecommendations = ordered.SelectMany(o => o.Recommendations).GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.First()).Take(BlackBoxLimits.MaxRecommendations).ToList();
            analysis.ArmadaImprovements = ordered.SelectMany(o => o.Improvements).Take(BlackBoxLimits.MaxImprovements).ToList();
            analysis.RecoveryPaths = RankPaths(ordered);
            analysis.Triage = Triage(ordered);
            analysis.Summary = Summarize(record, analysis);
            analysis.SummaryIsModelWritten = false;
            return analysis;
        }

        /// <summary>
        /// Compute the triage verdict from votes: the highest total weight wins; a tie at the top reports Unknown and lists
        /// the tied candidates; no votes reports Unknown with Low confidence.
        /// </summary>
        /// <param name="votes">Votes.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="ArgumentNullException">Thrown when votes is null.</exception>
        public static BlackBoxTriageVerdict Tally(IEnumerable<BlackBoxTriageVote> votes)
        {
            if (votes == null) throw new ArgumentNullException(nameof(votes));
            List<BlackBoxTriageVote> list = votes.Where(v => v != null && v.Weight > 0 && v.Verdict != BlackBoxTriageEnum.Unknown).Take(BlackBoxLimits.MaxTriageVotes).ToList();
            BlackBoxTriageVerdict verdict = new BlackBoxTriageVerdict { Votes = list, Verdict = BlackBoxTriageEnum.Unknown, Confidence = BlackBoxConfidenceEnum.Low };
            if (list.Count == 0) return verdict;
            List<KeyValuePair<BlackBoxTriageEnum, int>> totals = list.GroupBy(v => v.Verdict)
                .Select(g => new KeyValuePair<BlackBoxTriageEnum, int>(g.Key, g.Sum(v => v.Weight)))
                .OrderByDescending(p => p.Value).ThenBy(p => (int)p.Key).ToList();
            int top = totals[0].Value;
            List<BlackBoxTriageEnum> tied = totals.Where(p => p.Value == top).Select(p => p.Key).ToList();
            if (tied.Count > 1)
            {
                verdict.TiedWith = tied;
                return verdict;
            }
            int second = totals.Count > 1 ? totals[1].Value : 0;
            verdict.Verdict = totals[0].Key;
            verdict.Confidence = top >= 4 && top - second >= 2 ? BlackBoxConfidenceEnum.High : (top >= 2 ? BlackBoxConfidenceEnum.Medium : BlackBoxConfidenceEnum.Low);
            return verdict;
        }

        #endregion

        #region Private-Methods

        private static bool SafeMatch(BlackBoxRule rule, BlackBoxRecord record, ICollection<string>? diagnostics)
        {
            try
            {
                return rule.Match(record);
            }
            catch (Exception e) when (e is NullReferenceException || e is InvalidOperationException || e is ArgumentException || e is FormatException || e is IndexOutOfRangeException)
            {
                diagnostics?.Add("rule " + rule.Id + " match threw " + e.GetType().Name + ": " + e.Message + "; treated as no match");
                return false;
            }
        }

        private static BlackBoxRuleOutput? Render(BlackBoxRule rule, BlackBoxRecord record, ICollection<string>? diagnostics)
        {
            List<string> missing = new List<string>();
            string? why = BlackBoxRuleTemplate.Render(rule.Why, record, missing);
            if (why == null)
            {
                diagnostics?.Add("rule " + rule.Id + " skipped: missing placeholder(s) " + String.Join(", ", missing.Distinct()) + " in its why statement");
                return null;
            }

            BlackBoxRuleOutput output = new BlackBoxRuleOutput(rule);
            output.Finding = new BlackBoxFinding
            {
                RuleId = rule.Id,
                Statement = why,
                Confidence = rule.Confidence,
                EvidenceRefs = rule.EvidenceRefs.Take(BlackBoxLimits.MaxEvidenceRefs).ToList()
            };

            foreach (BlackBoxRuleRecommendation template in rule.Recommendations)
            {
                missing.Clear();
                BlackBoxConfigRecommendation? rec = RenderRecommendation(rule, template, record, missing);
                if (rec == null && missing.Count == 0) continue;
                if (rec == null)
                {
                    if (!template.Optional) { diagnostics?.Add("rule " + rule.Id + " skipped: recommendation " + template.Key + " misses " + String.Join(", ", missing.Distinct())); return null; }
                    diagnostics?.Add("rule " + rule.Id + ": recommendation " + template.Key + " left out (missing " + String.Join(", ", missing.Distinct()) + ")");
                    continue;
                }
                output.Recommendations.Add(rec);
            }

            foreach (BlackBoxRuleImprovement template in rule.Improvements)
            {
                missing.Clear();
                BlackBoxImprovement? imp = RenderImprovement(rule, template, record, missing);
                if (imp == null)
                {
                    if (!template.Optional) { diagnostics?.Add("rule " + rule.Id + " skipped: improvement misses " + String.Join(", ", missing.Distinct())); return null; }
                    diagnostics?.Add("rule " + rule.Id + ": improvement left out (missing " + String.Join(", ", missing.Distinct()) + ")");
                    continue;
                }
                output.Improvements.Add(imp);
            }

            foreach (BlackBoxRulePath template in rule.Paths.OrderBy(p => p.Priority))
            {
                if (template.When != null)
                {
                    bool offered;
                    try { offered = template.When(record); }
                    catch (Exception e) when (e is NullReferenceException || e is InvalidOperationException || e is ArgumentException)
                    {
                        offered = false;
                    }
                    if (!offered) continue;
                }
                missing.Clear();
                BlackBoxRecoveryPath? path = RenderPath(rule, template, record, missing);
                if (path == null)
                {
                    if (!template.Optional) { diagnostics?.Add("rule " + rule.Id + " skipped: recovery path " + template.Key + " misses " + String.Join(", ", missing.Distinct())); return null; }
                    diagnostics?.Add("rule " + rule.Id + ": recovery path " + template.Key + " left out (missing " + String.Join(", ", missing.Distinct()) + ")");
                    continue;
                }
                output.Paths.Add(path);
            }
            return output;
        }

        private static BlackBoxConfigRecommendation? RenderRecommendation(BlackBoxRule rule, BlackBoxRuleRecommendation t, BlackBoxRecord record, List<string> missing)
        {
            string? suggested = t.SuggestedValue == null ? null : BlackBoxRuleTemplate.Render(t.SuggestedValue, record, missing);
            string? rationale = t.Rationale == null ? null : BlackBoxRuleTemplate.Render(t.Rationale, record, missing);
            string? how = t.HowToApply == null ? null : BlackBoxRuleTemplate.Render(t.HowToApply, record, missing);
            Dictionary<string, string>? args = t.ApplyTool == null ? null : BlackBoxRuleTemplate.RenderArguments(t.ApplyArguments, record, missing);
            if (missing.Count > 0) return null;
            string? current = BlackBoxRuleFacts.Config(record, t.Setting)?.Value;
            if (t.SkipWhenCurrent && suggested != null && current != null && String.Equals(current.Trim(), suggested.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            BlackBoxConfigRecommendation rec = new BlackBoxConfigRecommendation
            {
                Id = rule.ComponentId(t.Key),
                Setting = t.Setting,
                Scope = t.Scope,
                CurrentValue = current,
                SuggestedValue = suggested,
                Rationale = rationale,
                RuleId = rule.Id,
                HowToApply = how
            };
            if (t.ApplyTool != null && args != null && BlackBoxActionCommands.IsSupported(t.ApplyTool))
                rec.ApplyAction = new BlackBoxAction { Tool = t.ApplyTool, Arguments = args };
            return rec;
        }

        private static BlackBoxImprovement? RenderImprovement(BlackBoxRule rule, BlackBoxRuleImprovement t, BlackBoxRecord record, List<string> missing)
        {
            string? hypothesis = BlackBoxRuleTemplate.Render(t.Hypothesis, record, missing);
            string? evidence = t.Evidence == null ? null : BlackBoxRuleTemplate.Render(t.Evidence, record, missing);
            if (hypothesis == null || missing.Count > 0) return null;
            return new BlackBoxImprovement
            {
                Area = t.Area,
                Hypothesis = hypothesis,
                Evidence = evidence,
                SourcePointers = t.SourcePointers.Take(BlackBoxLimits.MaxSourcePointers).ToList(),
                Severity = t.Severity,
                RuleId = rule.Id,
                KnownIssue = t.KnownIssue
            };
        }

        private static BlackBoxRecoveryPath? RenderPath(BlackBoxRule rule, BlackBoxRulePath t, BlackBoxRecord record, List<string> missing)
        {
            string? title = BlackBoxRuleTemplate.Render(t.Title, record, missing);
            List<string> preconditions = new List<string>();
            foreach (string pre in t.Preconditions.Take(BlackBoxLimits.MaxPreconditions))
            {
                string? text = BlackBoxRuleTemplate.Render(pre, record, missing);
                if (text != null) preconditions.Add(text);
            }
            string? expected = t.ExpectedOutcome == null ? null : BlackBoxRuleTemplate.Render(t.ExpectedOutcome, record, missing);
            List<BlackBoxRecoveryStep> steps = new List<BlackBoxRecoveryStep>();
            int order = 1;
            foreach (BlackBoxRuleStep st in t.Steps.Take(BlackBoxLimits.MaxRecoverySteps))
            {
                string? description = BlackBoxRuleTemplate.Render(st.Description, record, missing);
                BlackBoxAction? action = null;
                if (st.Tool != null)
                {
                    if (!BlackBoxActionCommands.IsSupported(st.Tool)) throw new InvalidOperationException("Rule " + rule.Id + " path " + t.Key + " names unsupported tool " + st.Tool + ".");
                    Dictionary<string, string>? args = BlackBoxRuleTemplate.RenderArguments(st.Arguments, record, missing);
                    if (args != null) action = new BlackBoxAction { Tool = st.Tool, Arguments = args };
                }
                string? cli = st.Cli != null ? BlackBoxRuleTemplate.Render(st.Cli, record, missing) : (action != null ? BlackBoxActionCommands.Cli(action) : null);
                if (description == null) continue;
                steps.Add(new BlackBoxRecoveryStep
                {
                    Order = order++,
                    Description = description,
                    Action = action,
                    McpCommand = action != null ? BlackBoxRuleTemplate.McpCommand(action) : null,
                    RestCommand = action != null ? BlackBoxActionCommands.Rest(action) : null,
                    CliCommand = cli,
                    DataLossRisk = st.DataLossRisk || (action != null && BlackBoxActionCommands.DataLossTools.Contains(action.Tool, StringComparer.Ordinal))
                });
            }
            if (title == null || missing.Count > 0) return null;
            return new BlackBoxRecoveryPath
            {
                Id = rule.ComponentId(t.Key),
                Title = title,
                Preconditions = preconditions,
                Steps = steps,
                Action = steps.LastOrDefault(s => s.Action != null)?.Action,
                Risk = t.Risk,
                DataLossRisk = steps.Any(s => s.DataLossRisk),
                ExpectedOutcome = expected,
                RuleId = rule.Id
            };
        }

        private static List<BlackBoxRecoveryPath> RankPaths(List<BlackBoxRuleOutput> ordered)
        {
            List<BlackBoxRecoveryPath> ranked = new List<BlackBoxRecoveryPath>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlackBoxRecoveryPath path in ordered.SelectMany(o => o.Paths))
            {
                string signature = String.Join(" | ", path.Steps.Select(s => s.Action != null ? BlackBoxRuleTemplate.McpCommand(s.Action) : "manual:" + s.Description));
                if (!seen.Add(signature)) continue;
                path.Rank = ranked.Count + 1;
                ranked.Add(path);
                if (ranked.Count >= BlackBoxLimits.MaxRecoveryPaths) break;
            }
            return ranked;
        }

        private static BlackBoxTriageVerdict Triage(List<BlackBoxRuleOutput> ordered)
        {
            return Tally(ordered.Where(o => o.Rule.Weight > 0 && o.Rule.Vote != BlackBoxTriageEnum.Unknown)
                .Select(o => new BlackBoxTriageVote { RuleId = o.Rule.Id, Verdict = o.Rule.Vote, Weight = o.Rule.Weight }));
        }

        private static string Summarize(BlackBoxRecord record, BlackBoxAnalysis analysis)
        {
            List<string> sentences = new List<string>();
            BlackBoxSubject? s = record.Subject;
            string subject = s?.SubjectType == BlackBoxSubjectTypeEnum.Voyage ? "Voyage" : "Mission";
            string title = s?.Title != null ? " \"" + OneLine(s.Title) + "\"" : String.Empty;
            string id = s?.SubjectType == BlackBoxSubjectTypeEnum.Voyage ? (s?.VoyageId ?? String.Empty) : (s?.MissionId ?? String.Empty);
            string label = subject + title + (id.Length > 0 ? " (" + id + ")" : String.Empty);
            if (BlackBoxRuleFacts.IsEmpty(record))
            {
                List<string> absent = new List<string>();
                if (record.Site == null || record.Site.Step == BlackBoxStepEnum.Unknown) absent.Add("the failure site");
                if (record.Mechanism == null || (record.Mechanism.ExceptionType == null && record.Mechanism.ExitCode == null && record.Mechanism.FailingCommand == null)) absent.Add("the mechanism");
                if (record.Classification?.RawReason == null) absent.Add("the reason");
                if (record.Workspace == null) absent.Add("the workspace");
                sentences.Add(label + " failed, but the black box knows little about it: " + String.Join(", ", absent) + (absent.Count == 1 ? " is" : " are") + " missing.");
            }
            else
            {
                BlackBoxPhaseEnum phase = BlackBoxRuleFacts.Phase(record);
                BlackBoxStepEnum step = BlackBoxRuleFacts.Step(record);
                MissionFailureKindEnum? kind = BlackBoxRuleFacts.Kind(record);
                sentences.Add(label + " failed during " + phase + " at step " + step + (kind.HasValue ? " (kind " + kind.Value + ")" : String.Empty) + ".");
            }
            foreach (BlackBoxFinding f in analysis.Why.Take(2)) sentences.Add(EndSentence(f.Statement));
            BlackBoxTriageVerdict? t = analysis.Triage;
            if (t != null)
            {
                if (t.Verdict != BlackBoxTriageEnum.Unknown) sentences.Add("Triage: " + t.Verdict + " (" + t.Confidence.ToString().ToLowerInvariant() + " confidence).");
                else if (t.TiedWith.Count > 1) sentences.Add("Triage is undecided: a tie between " + String.Join(" and ", t.TiedWith) + ".");
                else sentences.Add("Triage is undecided; judge it from the evidence.");
            }
            BlackBoxRecoveryPath? first = analysis.RecoveryPaths.FirstOrDefault();
            if (first != null) sentences.Add("Suggested next step: " + EndSentence(first.Title.Substring(0, 1).ToLowerInvariant() + first.Title.Substring(1)).TrimEnd('.') + " (" + first.Id + ").");
            BlackBoxConfigRecommendation? rec = analysis.ConfigRecommendations.FirstOrDefault(r => r.SuggestedValue != null);
            if (rec != null && sentences.Count < 6) sentences.Add("To prevent it, set " + rec.Setting + " to " + rec.SuggestedValue + " on the " + rec.Scope.ToString().ToLowerInvariant() + ".");
            string summary = String.Join(" ", sentences.Take(6));
            return BlackBoxLimits.Clip(summary.Replace('—', '-').Replace('–', '-'), BlackBoxLimits.MaxSummaryChars) ?? String.Empty;
        }

        private static string EndSentence(string text)
        {
            string t = OneLine(text).Trim();
            if (t.Length == 0) return t;
            char last = t[t.Length - 1];
            return last == '.' || last == '!' || last == '?' ? t : t + ".";
        }

        private static string OneLine(string text)
        {
            return text.Replace("\r", " ").Replace("\n", " ");
        }

        #endregion
    }
}
