namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The format-only repair turn (RS-6.4, MODEL_HETEROGENEITY principle 4). A stage whose work is done but whose report
    /// broke the output contract gets one tool-less call that restates its own output in the contract's format. The
    /// restatement is accepted only when it parses and cites nothing the original lacked: no PASS the original did not
    /// state, no evidence or findings it did not contain, and no mission it did not plan.
    /// </summary>
    public static class FormatRepair
    {
        #region Public-Members

        /// <summary>
        /// The longest tail of the original output quoted in the format call.
        /// </summary>
        public const int MaxQuotedOutputChars = 24000;

        /// <summary>
        /// How long the format call may run, in milliseconds.
        /// </summary>
        public const int TimeoutMs = 180000;

        #endregion

        #region Private-Members

        private const int MaxQuotedErrors = 20;
        private const int MinScenarioWordLength = 4;
        private const double MinScenarioGroundedShare = 0.6;

        private static readonly Regex _AffectedCaseField = new Regex(
            @"^\s*(?:[-*]\s*)?(?:\*\*|__)?(?<label>File|Scenario)(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(?<value>.*?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex _Word = new Regex(@"[A-Za-z0-9_]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _Whitespace = new Regex(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the prompt for the format call: the stored output, the parser's errors, and the contract.
        /// </summary>
        /// <param name="persona">Persona of the stage whose output is restated.</param>
        /// <param name="originalOutput">The stage's stored output.</param>
        /// <param name="contract">The required output format.</param>
        /// <param name="errors">What was wrong with the output, in order.</param>
        /// <returns>The prompt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when contract or errors is null.</exception>
        public static string BuildPrompt(string? persona, string? originalOutput, string contract, IReadOnlyList<string> errors)
        {
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            string original = originalOutput ?? String.Empty;
            if (original.Length > MaxQuotedOutputChars)
                original = "[...earlier output omitted...]\n" + original.Substring(original.Length - MaxQuotedOutputChars);

            StringBuilder prompt = new StringBuilder();
            prompt.Append("You are fixing the format of a report that an Armada ").Append(String.IsNullOrWhiteSpace(persona) ? "stage" : persona!.Trim())
                .Append(" agent already wrote. The work and the review are finished. Do not do any new work: do not run tools, commands or tests, and do not read or change files.\n");
            prompt.Append("Restate the report below in the required format. Use only the verdicts, findings, files, checks and missions that appear in the report. ");
            prompt.Append("Do not add a verdict, evidence, findings or missions the report does not contain. If the report does not state something the format requires, leave it out.\n\n");
            prompt.Append("## Problems found in the report\n");
            int count = 0;
            foreach (string error in errors)
            {
                if (String.IsNullOrWhiteSpace(error)) continue;
                if (++count > MaxQuotedErrors) break;
                prompt.Append(count).Append(". ").Append(error.Trim()).Append('\n');
            }
            if (count == 0) prompt.Append("1. The report does not follow the required format.\n");
            prompt.Append("\n## Required format\n").Append(contract.Trim()).Append("\n\n");
            prompt.Append("## Report\n<<<REPORT\n").Append(original.TrimEnd()).Append("\nREPORT\n\n");
            prompt.Append("Reply with the corrected report only.");
            return prompt.ToString();
        }

        /// <summary>
        /// Accept a restated Judge review only when it has one canonical verdict the original stated, never a
        /// SPEC_CONFLICT, a PASS only when the original cited evidence and stated no blocking verdict, a block only with an
        /// affected case the original contained, and no file the original did not cite.
        /// </summary>
        /// <param name="original">The Judge's original output.</param>
        /// <param name="restated">The format call's answer.</param>
        /// <param name="verdict">The restated verdict, when accepted.</param>
        /// <param name="rejection">Why the restatement was rejected, when it was.</param>
        /// <returns>True when the restatement may be appended.</returns>
        public static bool TryAcceptJudge(string? original, string? restated, out JudgeVerdictEnum verdict, out string? rejection)
        {
            verdict = JudgeVerdictEnum.None;
            rejection = null;
            if (String.IsNullOrWhiteSpace(restated)) { rejection = "the format call returned nothing"; return false; }

            JudgeVerdictEnum stated = JudgeVerdictParser.Parse(restated);
            if (stated == JudgeVerdictEnum.None) { rejection = "the restatement has no single canonical verdict line"; return false; }
            if (stated == JudgeVerdictEnum.SpecConflict) { rejection = "a format repair never produces SPEC_CONFLICT"; return false; }
            if (!OriginalStates(original, stated)) { rejection = "the restated verdict " + stated + " does not appear in the original output"; return false; }

            if (stated == JudgeVerdictEnum.Pass)
            {
                if (OriginalStates(original, JudgeVerdictEnum.Fail) || OriginalStates(original, JudgeVerdictEnum.NeedsRevision))
                {
                    rejection = "the original output also states a blocking verdict, so a PASS cannot be chosen by a format repair";
                    return false;
                }
                if (!JudgeEvidence.HasPassEvidence(original)) { rejection = "the original output cites no evidence, so a PASS cannot be restated"; return false; }
            }
            else if (!JudgeContract.ExhibitsAffectedCase(restated))
            {
                rejection = "the restated block has no affected case";
                return false;
            }

            if (!CitesOnlyOriginal(original, restated, out rejection)) return false;
            verdict = stated;
            return true;
        }

        /// <summary>
        /// Accept a restated verifier block only when it still reports the block, exhibits an affected case, and cites
        /// nothing the original did not.
        /// </summary>
        /// <param name="original">The stage's original output.</param>
        /// <param name="restated">The format call's answer.</param>
        /// <param name="rejection">Why the restatement was rejected, when it was.</param>
        /// <returns>True when the restatement may be appended.</returns>
        public static bool TryAcceptVerifierBlock(string? original, string? restated, out string? rejection)
        {
            rejection = null;
            if (String.IsNullOrWhiteSpace(restated)) { rejection = "the format call returned nothing"; return false; }
            if (!VerifierContract.IsBlocked(restated)) { rejection = "the restatement dropped the " + VerifierContract.BlockedMarker + " line"; return false; }
            if (!JudgeContract.ExhibitsAffectedCase(restated)) { rejection = "the restated block has no affected case"; return false; }
            return CitesOnlyOriginal(original, restated, out rejection);
        }

        /// <summary>
        /// Accept a restated Architect plan only when it parses and every mission title already appears in the original.
        /// </summary>
        /// <param name="original">The Architect's original output.</param>
        /// <param name="restated">The format call's answer.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed parent chain.</param>
        /// <param name="rejection">Why the restatement was rejected, when it was.</param>
        /// <returns>True when the restatement may be appended.</returns>
        public static bool TryAcceptArchitectPlan(string? original, string? restated, int maxHierarchyDepth, out string? rejection)
        {
            rejection = null;
            if (String.IsNullOrWhiteSpace(restated)) { rejection = "the format call returned nothing"; return false; }
            ArchitectPlanParseResult result = ArchitectPlanParser.Parse(restated, maxHierarchyDepth);
            if (result.Status != ArchitectPlanParseStatusEnum.Parsed || result.Plan == null)
            {
                rejection = "the restated plan does not parse: " + (result.Error ?? "no armada-plan block");
                return false;
            }

            string normalizedOriginal = Normalize(original);
            foreach (ArchitectPlanMission mission in result.Plan.Missions)
            {
                string title = Normalize(mission.Title);
                if (title.Length == 0 || !normalizedOriginal.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    rejection = "the restated plan has a mission the original did not plan: " + (mission.Title ?? "(untitled)");
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Private-Methods

        private static bool OriginalStates(string? original, JudgeVerdictEnum verdict)
        {
            if (String.IsNullOrEmpty(original)) return false;
            string pattern = verdict switch
            {
                JudgeVerdictEnum.Pass => @"\bPASS(?:ED)?\b|\bverdict\W{0,6}pass\b",
                JudgeVerdictEnum.Fail => @"\bFAIL(?:ED)?\b|\bverdict\W{0,6}fail\b",
                JudgeVerdictEnum.NeedsRevision => @"\bNEEDS[ _-]REVISION\b|\bverdict\W{0,6}needs[ _-]revision\b",
                _ => null
            } ?? String.Empty;
            if (pattern.Length == 0) return false;
            // Upper-case tokens are matched as written; the "verdict: x" form in any case.
            return Regex.IsMatch(original, pattern, RegexOptions.CultureInvariant)
                || Regex.IsMatch(original, pattern.Substring(pattern.IndexOf('|') + 1), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool CitesOnlyOriginal(string? original, string restated, out string? rejection)
        {
            rejection = null;
            string source = original ?? String.Empty;
            string normalizedSource = source.Replace('\\', '/');
            foreach (string path in JudgeEvidence.FileReferences(restated))
            {
                if (!normalizedSource.Contains(path, StringComparison.OrdinalIgnoreCase))
                {
                    rejection = "the restatement cites " + path + ", which the original output does not";
                    return false;
                }
            }

            if (JudgeEvidence.CitesCheck(restated) && !JudgeEvidence.CitesCheck(source))
            {
                rejection = "the restatement names a check the original output does not";
                return false;
            }

            HashSet<string> sourceWords = new HashSet<string>(_Word.Matches(source).Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
            foreach (Match field in _AffectedCaseField.Matches(restated))
            {
                if (!String.Equals(field.Groups["label"].Value, "Scenario", StringComparison.OrdinalIgnoreCase)) continue;
                List<string> words = _Word.Matches(field.Groups["value"].Value).Select(m => m.Value).Where(w => w.Length >= MinScenarioWordLength).ToList();
                if (words.Count == 0) continue;
                int grounded = words.Count(w => sourceWords.Contains(w));
                if (grounded < words.Count * MinScenarioGroundedShare)
                {
                    rejection = "the restated scenario is not taken from the original output";
                    return false;
                }
            }

            return true;
        }

        private static string Normalize(string? text)
        {
            return String.IsNullOrWhiteSpace(text) ? String.Empty : _Whitespace.Replace(text!.Trim(), " ");
        }

        #endregion
    }
}
