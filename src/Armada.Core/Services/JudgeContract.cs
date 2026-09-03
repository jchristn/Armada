namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Pure, side-effect-free definition of the bounded three-lens Judge contract. A Judge reviews work
    /// through exactly three lenses -- correctness, blast radius, and source fidelity -- and must exhibit a
    /// concrete affected case before it is allowed to block. Keeping the contract in one pure place lets the
    /// prompt builders, the completion gate, and the tests share a single source of truth and unit-test it
    /// without a dock or database.
    /// </summary>
    public static class JudgeContract
    {
        #region Public-Members

        /// <summary>
        /// The correctness lens: is the change logically correct for the inputs it can receive?
        /// </summary>
        public const string LensCorrectness = "Correctness";

        /// <summary>
        /// The blast-radius lens: what else could this change break, and how far do its effects reach?
        /// </summary>
        public const string LensBlastRadius = "Blast Radius";

        /// <summary>
        /// The source-fidelity lens: does the change faithfully implement the mission, stay in scope, and
        /// match the real codebase (no invented behavior)?
        /// </summary>
        public const string LensSourceFidelity = "Source Fidelity";

        /// <summary>
        /// The section a blocking verdict must include to exhibit a concrete affected case.
        /// </summary>
        public const string AffectedCaseSection = "Affected Case";

        /// <summary>
        /// The three required lens section headings, in order.
        /// </summary>
        public static IReadOnlyList<string> RequiredLenses { get; } = new List<string>
        {
            LensCorrectness,
            LensBlastRadius,
            LensSourceFidelity
        };

        #endregion

        #region Private-Members

        private const int MinNarrativeChars = 120;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the Judge output contains a section with the given heading (tolerating heading level,
        /// list markers, numbering, and bold/italic/code wrapping).
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <param name="sectionName">Section heading to look for.</param>
        /// <returns>True when the section heading is present.</returns>
        public static bool ContainsSection(string? output, string sectionName)
        {
            if (String.IsNullOrWhiteSpace(output) || String.IsNullOrWhiteSpace(sectionName)) return false;

            string pattern =
                @"(?im)^\s*(?:#{1,6}\s*)?(?:[-*]\s*)?(?:\d+\.\s*)?(?:\*\*|__|`)?"
                + Regex.Escape(sectionName)
                + @"(?:\*\*|__|`)?\s*(?::|-)?(?:\s|$)";

            return Regex.IsMatch(output, pattern);
        }

        /// <summary>
        /// Whether the output contains all three required lens sections. Missing lenses are reported for a
        /// precise failure reason.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <param name="missing">Populated with the lenses that were absent.</param>
        /// <returns>True when every required lens is present.</returns>
        public static bool HasAllLenses(string? output, out List<string> missing)
        {
            missing = new List<string>();
            foreach (string lens in RequiredLenses)
            {
                if (!ContainsSection(output, lens)) missing.Add(lens);
            }
            return missing.Count == 0;
        }

        /// <summary>
        /// Whether a blocking verdict exhibits a concrete affected case: an "Affected Case" section whose
        /// body cites a real reference (a file path, a file:line, or a described scenario/repro). A block
        /// that only asserts a vague concern does not satisfy this.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <returns>True when a concrete affected case is exhibited.</returns>
        public static bool ExhibitsAffectedCase(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return false;

            string body = ExtractSectionBody(output!, AffectedCaseSection);
            if (String.IsNullOrWhiteSpace(body)) return false;

            // A concrete file reference (path with an extension, optionally :line).
            if (Regex.IsMatch(body, @"[\w./\\-]+\.[A-Za-z]{1,8}(?::\d+)?")) return true;

            // Or an explicit scenario / reproduction phrasing that names the triggering condition.
            if (Regex.IsMatch(body, @"(?i)\b(when|given|if the|input|scenario|repro|reproduce|call|invoke|passing|returns|throws)\b")) return true;

            return false;
        }

        /// <summary>
        /// Whether a PASS verdict is substantiated: it must contain all three lenses and enough narrative to
        /// justify approval.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <param name="narrative">The substantive review narrative (telemetry and verdict lines removed).</param>
        /// <param name="failureReason">Populated with why the PASS is unsubstantiated, when it is.</param>
        /// <returns>True when the PASS is substantiated.</returns>
        public static bool ValidatePass(string? output, string narrative, out string? failureReason)
        {
            failureReason = null;

            if (String.IsNullOrWhiteSpace(output))
            {
                failureReason = "Judge PASS verdict missing review output";
                return false;
            }

            if (!HasAllLenses(output, out List<string> missing))
            {
                failureReason = "Judge PASS verdict missing required lens sections: " + String.Join(", ", missing);
                return false;
            }

            if ((narrative ?? String.Empty).Length < MinNarrativeChars)
            {
                failureReason = "Judge PASS verdict review is too short to justify approval";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The required-output-contract text appended to a Judge captain's instructions.
        /// </summary>
        /// <returns>Contract text.</returns>
        public static string OutputContract()
        {
            return
                "Review the work through exactly three lenses, each as its own section: " +
                "`## Correctness` (is the change logically correct for the inputs it can receive?), " +
                "`## Blast Radius` (what else could this change break, and how far do its effects reach?), and " +
                "`## Source Fidelity` (does it faithfully implement the mission, stay in scope, and match the real codebase without inventing behavior?). " +
                "End with a `## Verdict` section and exactly one standalone line `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`. " +
                "To block (FAIL or NEEDS_REVISION) you MUST include a `## Affected Case` section that exhibits one concrete affected case -- a specific file, line, or scenario where the change is wrong or unsafe. " +
                "A blocking verdict without a concrete affected case is not accepted. Do not reply with only a verdict line.";
        }

        /// <summary>
        /// The fallback persona prompt used when no template is available.
        /// </summary>
        /// <returns>Persona prompt text.</returns>
        public static string PersonaPromptFallback()
        {
            return
                "You are an Armada judge agent. Review the completed work through three lenses -- correctness, " +
                "blast radius, and source fidelity -- and assume there may be a hidden defect. " +
                "Use `## Correctness`, `## Blast Radius`, `## Source Fidelity`, and `## Verdict` sections. " +
                "To block, add a `## Affected Case` section exhibiting one concrete affected case (a specific file, line, or scenario). " +
                "End with exactly one standalone [ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION line.";
        }

        /// <summary>
        /// The one-line role summary used in launch prompts.
        /// </summary>
        /// <returns>Role summary text.</returns>
        public static string RoleSummary()
        {
            return
                "You are an Armada judge agent. Review through `## Correctness`, `## Blast Radius`, and `## Source Fidelity` sections, " +
                "add a `## Affected Case` section with a concrete case if you block, and end with exactly one standalone " +
                "[ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION line.";
        }

        #endregion

        #region Private-Methods

        private static string ExtractSectionBody(string output, string sectionName)
        {
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            string headingPattern =
                @"(?i)^\s*(?:#{1,6}\s*)?(?:[-*]\s*)?(?:\d+\.\s*)?(?:\*\*|__|`)?"
                + Regex.Escape(sectionName)
                + @"(?:\*\*|__|`)?\s*(?::|-)?\s*$";
            string anyHeadingPattern = @"^\s*#{1,6}\s+\S";

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], headingPattern))
                {
                    start = i + 1;
                    break;
                }
            }
            if (start < 0) return String.Empty;

            List<string> body = new List<string>();
            for (int i = start; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], anyHeadingPattern)) break;
                body.Add(lines[i]);
            }
            return String.Join("\n", body).Trim();
        }

        #endregion
    }
}
