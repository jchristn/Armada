namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Reads a Judge mission's verdict from its output using only the structured protocol line
    /// <c>[ARMADA:VERDICT] PASS|FAIL|NEEDS_REVISION|SPEC_CONFLICT</c>, which must be a whole line on its own outside any fenced code
    /// block (fence state resets at the output truncation marker). Prose ("Verdict: PASS", "### Verdict: **PASS**"), bare PASS/FAIL lines (for example test-runner output),
    /// inline mentions, and quoted examples are never verdicts. The contract asks for exactly one verdict line: a
    /// repeated identical line is accepted, but conflicting verdict lines are treated as no verdict so that an echoed
    /// line can never override the Judge's own.
    /// </summary>
    public static class JudgeVerdictParser
    {
        #region Private-Members

        private static readonly Regex _VerdictLine = new Regex(
            @"^\[ARMADA:VERDICT\][ \t]+(?<verdict>PASS|FAIL|NEEDS_REVISION|SPEC_CONFLICT)[ \t]*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the structured verdict from Judge output.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <returns>The verdict, or <see cref="JudgeVerdictEnum.None"/> when absent or ambiguous.</returns>
        public static JudgeVerdictEnum Parse(string? output)
        {
            if (String.IsNullOrEmpty(output)) return JudgeVerdictEnum.None;

            HashSet<JudgeVerdictEnum> found = new HashSet<JudgeVerdictEnum>();
            bool inFence = false;
            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                // A fence cut open by output truncation ends at the truncation marker, not at the end of the output.
                if (ProtocolLines.IsTruncationMarker(line))
                {
                    inFence = false;
                    continue;
                }

                if (ProtocolLines.IsFence(line))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence) continue;

                JudgeVerdictEnum? verdict = ParseLine(line);
                if (verdict.HasValue) found.Add(verdict.Value);
            }

            if (found.Count != 1) return JudgeVerdictEnum.None;
            foreach (JudgeVerdictEnum only in found) return only;
            return JudgeVerdictEnum.None;
        }

        /// <summary>
        /// Parse one line as a structured verdict line.
        /// </summary>
        /// <param name="line">One output line (surrounding whitespace is ignored).</param>
        /// <returns>The verdict, or null when the line is not a verdict protocol line.</returns>
        public static JudgeVerdictEnum? ParseLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;

            Match match = _VerdictLine.Match(line.Trim());
            if (!match.Success) return null;

            switch (match.Groups["verdict"].Value.ToUpperInvariant())
            {
                case "PASS": return JudgeVerdictEnum.Pass;
                case "FAIL": return JudgeVerdictEnum.Fail;
                case "NEEDS_REVISION": return JudgeVerdictEnum.NeedsRevision;
                case "SPEC_CONFLICT": return JudgeVerdictEnum.SpecConflict;
                default: return null;
            }
        }

        #endregion
    }
}
