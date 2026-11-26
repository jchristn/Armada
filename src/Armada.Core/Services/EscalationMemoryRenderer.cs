namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.BlackBox;
    using Armada.Core.Models;

    /// <summary>
    /// Renders an <see cref="EscalationMemory"/> into the body of a mission's escalation note (RS-5.7), and finds its
    /// black box rule hint by running the rule catalog over an in-memory record of the failure. Thread-safe.
    /// </summary>
    public static class EscalationMemoryRenderer
    {
        /// <summary>Most characters of the previous attempt's output kept in the note.</summary>
        public const int MaxOutputTailChars = 2000;

        /// <summary>Most changed files listed in the note.</summary>
        public const int MaxChangedFiles = 25;

        private const string _FallbackRulePrefix = "BBX-UNKNOWN";

        /// <summary>
        /// Set <see cref="EscalationMemory.Hint"/> and <see cref="EscalationMemory.HintRuleId"/> from the strongest specific
        /// black box rule that matches the failure. Leaves them null when only the catch-all rule matches.
        /// </summary>
        /// <param name="memory">Memory to fill in.</param>
        /// <param name="catalog">Rule catalog, or null for the default.</param>
        /// <exception cref="ArgumentNullException">Thrown when memory is null.</exception>
        public static void ApplyHint(EscalationMemory memory, BlackBoxRuleCatalog? catalog = null)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            BlackBoxRecord record = new BlackBoxRecord
            {
                Site = new BlackBoxSite { Step = memory.Step, Phase = BlackBoxTaxonomy.PhaseOf(memory.Step) },
                Classification = new BlackBoxClassification { Kind = memory.FailureKind, RawReason = memory.Reason },
                Mechanism = new BlackBoxMechanism { RuntimeFailureKind = memory.RuntimeFailureKind, ExitCode = memory.ExitCode, CommandOutputTail = memory.OutputTail }
            };
            BlackBoxAnalysis analysis = BlackBoxAdvisor.Analyze(record, catalog ?? BlackBoxRuleCatalog.Default);
            BlackBoxFinding? finding = analysis.Why.FirstOrDefault(f => !f.RuleId.StartsWith(_FallbackRulePrefix, StringComparison.Ordinal));
            memory.HintRuleId = finding?.RuleId;
            memory.Hint = finding?.Statement;
        }

        /// <summary>
        /// Render the note body: what happened, then the failure kind, rule hint, changed files and output tail when known.
        /// </summary>
        /// <param name="memory">The memory.</param>
        /// <param name="toCaptainName">Name of the captain the mission escalates to.</param>
        /// <returns>Markdown, starting with the escalation heading.</returns>
        /// <exception cref="ArgumentNullException">Thrown when memory is null.</exception>
        public static string Render(EscalationMemory memory, string toCaptainName)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            StringBuilder sb = new StringBuilder();
            string from = String.IsNullOrWhiteSpace(memory.FromCaptainName) ? "The previous captain" : memory.FromCaptainName!;
            if (!String.IsNullOrWhiteSpace(memory.FromModel)) from += " (" + memory.FromModel + ")";
            sb.Append("## Escalated to ").Append(toCaptainName).Append('\n');
            sb.Append(from).Append(" could not finish this mission (").Append(memory.Reason).Append("). ");
            sb.Append("Its work so far is committed on this branch and may not build yet. ");
            sb.Append("Review it with git log and git diff against the base branch, fix what is wrong, and finish the mission.\n");

            List<string> facts = new List<string>();
            if (memory.FailureKind.HasValue || memory.RuntimeFailureKind.HasValue)
            {
                List<string> kind = new List<string>();
                if (memory.FailureKind.HasValue) kind.Add(memory.FailureKind.Value.ToString());
                if (memory.RuntimeFailureKind.HasValue) kind.Add("runtime " + memory.RuntimeFailureKind.Value);
                if (memory.ExitCode.HasValue) kind.Add("exit " + memory.ExitCode.Value);
                facts.Add("- Failure kind: " + String.Join(", ", kind));
            }
            if (!String.IsNullOrWhiteSpace(memory.Hint)) facts.Add("- Likely cause: " + memory.Hint!.Trim() + " (" + memory.HintRuleId + ")");
            if (memory.ChangedFiles != null)
            {
                if (memory.ChangedFiles.Count == 0)
                {
                    facts.Add("- Changed files: none");
                }
                else
                {
                    string files = String.Join(", ", memory.ChangedFiles.Take(MaxChangedFiles));
                    if (memory.ChangedFiles.Count > MaxChangedFiles) files += ", and " + (memory.ChangedFiles.Count - MaxChangedFiles) + " more";
                    facts.Add("- Changed files (" + memory.ChangedFiles.Count + "): " + files);
                }
            }
            if (facts.Count > 0 || !String.IsNullOrWhiteSpace(memory.OutputTail))
            {
                sb.Append("\n### What the previous attempt left\n");
                foreach (string fact in facts) sb.Append(fact).Append('\n');
                if (!String.IsNullOrWhiteSpace(memory.OutputTail))
                {
                    sb.Append("\nThe end of its output:\n\n```text\n").Append(memory.OutputTail!.Replace("```", "'''").TrimEnd()).Append("\n```\n");
                }
            }
            return sb.ToString().Replace("—", "-").TrimEnd();
        }
    }
}
