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
    /// Grades a reviewer's output (Judge, Linter) against a seeded set: which seeded defects it found, which findings match
    /// nothing (false positives), and whether the verdict is one the task expects. Pure; no I/O.
    /// </summary>
    public static class BatterySeededSetGrader
    {
        #region Private-Members

        private static readonly Regex _ListItem = new Regex(@"^\s*(?:[-*+]|\d+[.)])\s+(?<text>.+)$", RegexOptions.CultureInvariant);
        private static readonly Regex _NumberedItem = new Regex(@"^\s{0,3}\d+[.)]\s+", RegexOptions.CultureInvariant);
        private static readonly Regex _BoldHeading = new Regex(@"^\*\*(?<text>[^*]+?)\*\*:?$", RegexOptions.CultureInvariant);

        private static readonly string[] _NonBlockingWords = new[]
        {
            "non-blocking", "nonblocking", "optional", "suggestion", "nitpick", "nits", "improvement", "strength", "resolved", "praise"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Grade an output.
        /// </summary>
        /// <param name="output">The captain's output.</param>
        /// <param name="grader">SeededSet grader definition.</param>
        /// <returns>The grade.</returns>
        /// <exception cref="ArgumentNullException">Thrown when grader is null.</exception>
        public static BatteryGrade Grade(string? output, BatteryGraderDefinition grader)
        {
            if (grader == null) throw new ArgumentNullException(nameof(grader));
            BatteryGrade grade = new BatteryGrade();
            if (String.IsNullOrWhiteSpace(output))
            {
                grade.Outcome = BatteryTaskOutcomeEnum.Failed;
                grade.Score = 0;
                grade.Reason = "The captain produced no output.";
                grade.Summary = "No output to grade.";
                grade.Details.Missed = grader.Seeded.Where(s => s != null).Select(s => s.Id ?? String.Empty).ToList();
                return grade;
            }

            List<string> findings = ExtractFindings(output, grader.BlockingSections);
            List<string> anywhere = grader.MatchAnywhere ? ProseLines(output) : findings;
            List<BatterySeededItem> seeded = grader.Seeded.Where(s => s != null).ToList();

            foreach (BatterySeededItem item in seeded)
            {
                string? hit = anywhere.FirstOrDefault(text => Mentions(text, item)) ?? findings.FirstOrDefault(text => Mentions(text, item));
                if (hit != null) grade.Details.Matched.Add(new BatterySeededMatch { Id = item.Id ?? String.Empty, Finding = Trim(hit) });
                else grade.Details.Missed.Add(item.Id ?? String.Empty);
            }

            // Agent output: keep the first MaxConfigItems false positives and count every one, so the stored evidence is
            // bounded and the count (which decides the grade) is exact.
            foreach (string finding in findings)
            {
                if (seeded.Any(item => Mentions(finding, item))) continue;
                grade.Details.FalsePositiveCount++;
                if (grade.Details.FalsePositives.Count < CollectionLimits.MaxConfigItems) grade.Details.FalsePositives.Add(Trim(finding));
            }

            grade.Details.FindingCount = findings.Count;
            string? verdict = VerdictName(JudgeVerdictParser.Parse(output));
            grade.Details.Verdict = verdict;

            int required = grader.MinMatched ?? seeded.Count;
            int matched = grade.Details.Matched.Count;
            int falsePositives = grade.Details.FalsePositiveCount;
            bool verdictOk = grader.ExpectedVerdicts.Count == 0 || (verdict != null && grader.ExpectedVerdicts.Contains(verdict, StringComparer.Ordinal));

            List<string> reasons = new List<string>();
            if (!verdictOk)
                reasons.Add("verdict " + (verdict ?? "missing") + " is not one of " + String.Join(", ", grader.ExpectedVerdicts));
            if (matched < required) reasons.Add("found " + matched + " of " + seeded.Count + " seeded items (missed " + String.Join(", ", grade.Details.Missed) + ")");
            if (falsePositives > grader.MaxFalsePositives)
                reasons.Add(falsePositives + " false positive" + (falsePositives == 1 ? String.Empty : "s") + " (at most " + grader.MaxFalsePositives + " allowed)");

            grade.Score = seeded.Count > 0 ? (double)matched / seeded.Count : (reasons.Count == 0 ? 1.0 : 0.0);
            grade.Outcome = reasons.Count == 0 ? BatteryTaskOutcomeEnum.Passed : BatteryTaskOutcomeEnum.Failed;
            grade.Reason = reasons.Count == 0 ? null : Capitalize(String.Join("; ", reasons)) + ".";
            grade.Summary = "Found " + matched + " of " + seeded.Count + " seeded, " + falsePositives + " false positive"
                + (falsePositives == 1 ? String.Empty : "s") + ", verdict " + (verdict ?? "none") + ".";
            return grade;
        }

        /// <summary>
        /// The findings in an output. With blocking sections, each section whose heading names one of them is a finding
        /// (split at numbered items or sub-headings); without, every list item outside code fences and outside
        /// non-blocking sections (optional, suggestions, nits, strengths, resolved) is a finding.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="blockingSections">Headings of blocking sections, or empty.</param>
        /// <returns>Findings, in order.</returns>
        public static List<string> ExtractFindings(string? output, List<string>? blockingSections)
        {
            List<string> findings = new List<string>();
            if (String.IsNullOrWhiteSpace(output)) return findings;
            List<string> sections = (blockingSections ?? new List<string>()).Where(s => !String.IsNullOrWhiteSpace(s)).ToList();

            bool inFence = false;
            string? heading = null;
            int headingLevel = 0;
            bool inBlocking = false;
            StringBuilder? current = null;

            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence || trimmed.Contains("[ARMADA:", StringComparison.Ordinal)) continue;

                int level;
                string? headingText = HeadingOf(trimmed, out level);
                if (headingText != null)
                {
                    bool blockingHeading = sections.Any(s => headingText.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (inBlocking && !blockingHeading && level > headingLevel)
                    {
                        // A sub-heading inside a blocking section starts the next finding.
                        Flush(findings, ref current);
                        current = new StringBuilder(headingText);
                        continue;
                    }

                    Flush(findings, ref current);
                    heading = headingText;
                    headingLevel = level;
                    inBlocking = blockingHeading;
                    continue;
                }

                if (sections.Count > 0)
                {
                    if (!inBlocking || trimmed.Length == 0) continue;
                    if (_NumberedItem.IsMatch(line) && current != null && current.Length > 0) Flush(findings, ref current);
                    current ??= new StringBuilder();
                    if (current.Length > 0) current.Append(' ');
                    current.Append(trimmed);
                    continue;
                }

                if (IsNonBlocking(heading)) continue;
                Match item = _ListItem.Match(line);
                if (item.Success && !line.StartsWith("    ", StringComparison.Ordinal) && !line.StartsWith("\t", StringComparison.Ordinal))
                {
                    Flush(findings, ref current);
                    current = new StringBuilder(item.Groups["text"].Value.Trim());
                }
                else if (trimmed.Length == 0)
                {
                    Flush(findings, ref current);
                }
                else if (current != null)
                {
                    current.Append(' ').Append(trimmed);
                }
            }

            Flush(findings, ref current);
            return findings;
        }

        /// <summary>
        /// Every non-empty line outside code fences, without Armada marker lines.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <returns>Lines.</returns>
        public static List<string> ProseLines(string? output)
        {
            List<string> lines = new List<string>();
            if (String.IsNullOrWhiteSpace(output)) return lines;
            bool inFence = false;
            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence || trimmed.Length == 0 || trimmed.Contains("[ARMADA:", StringComparison.Ordinal)) continue;
                lines.Add(trimmed);
            }

            return lines;
        }

        /// <summary>
        /// Whether a text mentions a seeded item (contains one of its keywords, case-insensitive).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="item">Seeded item.</param>
        /// <returns>True when it does.</returns>
        public static bool Mentions(string? text, BatterySeededItem? item)
        {
            if (String.IsNullOrEmpty(text) || item == null) return false;
            string normalized = Regex.Replace(text, @"\s+", " ");
            return item.Keywords.Any(k => !String.IsNullOrWhiteSpace(k) && normalized.IndexOf(k.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// The manifest name of a parsed Judge verdict.
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>PASS, FAIL, NEEDS_REVISION, SPEC_CONFLICT, or null for none.</returns>
        public static string? VerdictName(JudgeVerdictEnum verdict)
        {
            switch (verdict)
            {
                case JudgeVerdictEnum.Pass: return "PASS";
                case JudgeVerdictEnum.Fail: return "FAIL";
                case JudgeVerdictEnum.NeedsRevision: return "NEEDS_REVISION";
                case JudgeVerdictEnum.SpecConflict: return "SPEC_CONFLICT";
                default: return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string? HeadingOf(string trimmed, out int level)
        {
            level = 0;
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                string text = trimmed.Substring(level).Trim().TrimEnd(':').Trim();
                return text.Length > 0 ? text : null;
            }

            Match bold = _BoldHeading.Match(trimmed);
            if (bold.Success)
            {
                level = 7;
                return bold.Groups["text"].Value.Trim().TrimEnd(':').Trim();
            }

            return null;
        }

        private static bool IsNonBlocking(string? heading)
        {
            if (String.IsNullOrEmpty(heading)) return false;
            return _NonBlockingWords.Any(w => heading.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void Flush(List<string> findings, ref StringBuilder? current)
        {
            if (current != null)
            {
                string text = current.ToString().Trim();
                if (text.Length > 0 && !IsNonBlockingItem(text)) findings.Add(text);
            }

            current = null;
        }

        private static bool IsNonBlockingItem(string text)
        {
            return text.StartsWith("(non-blocking)", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("non-blocking", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("optional", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("nit", StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string text)
        {
            return text.Length <= 300 ? text : text.Substring(0, 300) + "...";
        }

        private static string Capitalize(string text)
        {
            return text.Length == 0 ? text : Char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        #endregion
    }
}
