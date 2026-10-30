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
        /// Section heading a SPEC_CONFLICT verdict must carry: the criterion that cannot be satisfied as written and the
        /// evidence why.
        /// </summary>
        public const string SpecConflictSection = "Spec Conflict";

        /// <summary>
        /// Field label, inside the Affected Case section, that names the affected file (optionally with :line).
        /// </summary>
        public const string AffectedCaseFileField = "File";

        /// <summary>
        /// Field label, inside the Affected Case section, that describes the triggering scenario.
        /// </summary>
        public const string AffectedCaseScenarioField = "Scenario";

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

        private const int MinScenarioChars = 20;

        private static readonly Regex _AffectedCaseFieldLine = new Regex(
            @"^\s*(?:[-*]\s*)?(?:\*\*|__)?(?<label>File|Scenario)(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(?<value>.*?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _FileReference = new Regex(
            @"^`?(?<path>[^\s`]*[/\\.][^\s`]*?)(?::\d+(?::\d+)?)?`?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

            foreach (string line in output.Replace("\r\n", "\n").Split('\n'))
            {
                string normalized = NormalizeHeadingLine(line, out _);
                if (String.Equals(normalized, sectionName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
        /// Whether a blocking verdict exhibits a concrete affected case: an "Affected Case" section that contains a
        /// labeled field line, either <c>File: path[:line]</c> naming a file (a single path token containing a
        /// separator or an extension) or <c>Scenario: ...</c> describing the triggering inputs and the wrong result in
        /// at least a short sentence. The decision reads those labeled fields only; it never searches the prose for
        /// words such as "when" or "returns".
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <returns>True when a concrete affected case is exhibited.</returns>
        public static bool ExhibitsAffectedCase(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return false;

            string body = ExtractSectionBody(output!, AffectedCaseSection);
            if (String.IsNullOrWhiteSpace(body)) return false;

            foreach (string line in body.Split('\n'))
            {
                Match field = _AffectedCaseFieldLine.Match(line);
                if (!field.Success) continue;

                string label = field.Groups["label"].Value;
                string value = field.Groups["value"].Value.Trim();
                if (String.IsNullOrEmpty(value)) continue;

                if (String.Equals(label, AffectedCaseFileField, StringComparison.OrdinalIgnoreCase))
                {
                    if (_FileReference.IsMatch(value)) return true;
                }
                else if (value.Length >= MinScenarioChars)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A comparable key for the first concrete affected case a blocking verdict exhibits: its labeled field, lowercased,
        /// with whitespace collapsed and a trailing line number dropped from a file reference (the same defect moves lines
        /// as code changes). Two rounds that report the same key name the same unresolved case.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <returns>The key, or null when no concrete affected case is exhibited.</returns>
        public static string? AffectedCaseKey(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return null;
            string body = ExtractSectionBody(output!, AffectedCaseSection);
            if (String.IsNullOrWhiteSpace(body)) return null;

            foreach (string line in body.Split('\n'))
            {
                Match field = _AffectedCaseFieldLine.Match(line);
                if (!field.Success) continue;
                string label = field.Groups["label"].Value.ToLowerInvariant();
                string value = Regex.Replace(field.Groups["value"].Value.Trim(), @"\s+", " ").ToLowerInvariant();
                if (String.IsNullOrEmpty(value)) continue;
                if (String.Equals(label, AffectedCaseFileField, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_FileReference.IsMatch(value)) continue;
                    value = Regex.Replace(value, @":\d+(-\d+)?\s*$", String.Empty);
                }
                else if (value.Length < MinScenarioChars)
                {
                    continue;
                }
                return label + ":" + value;
            }

            return null;
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
                "Review cumulative changes against original acceptance criteria and every prior finding. " +
                "Use current source and relevant checks; distinguish executed evidence from upstream claims and unverified delivery. " +
                "Use `## Correctness`, `## Blast Radius`, `## Source Fidelity`, and `## Verdict`. " +
                "Report all concrete blocking defects; label optional improvements separately. " +
                "To block, add `## Affected Case` with `File: <path>[:line]` or `Scenario: <inputs and wrong result>`, " +
                "the violated requirement, expected/actual behavior, and required correction. " +
                "Mark prior findings resolved, superseded (why), or unresolved. " +
                "If an acceptance criterion cannot be satisfied as written (it contradicts another requirement or a dependency's verified behavior), do not request rework: add `## Spec Conflict` quoting the criterion and the evidence, and use `[ARMADA:VERDICT] SPEC_CONFLICT`; a person decides. " +
                "End with exactly one standalone `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, `[ARMADA:VERDICT] NEEDS_REVISION`, or `[ARMADA:VERDICT] SPEC_CONFLICT` line.";
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
                "To block, add a `## Affected Case` section exhibiting one concrete affected case as a `File: <path>[:line]` or `Scenario: <inputs and wrong result>` line. " +
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
                "add a `## Affected Case` section with a `File:` or `Scenario:` line if you block, and end with exactly one standalone " +
                "[ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION line. " +
                "Restore dependencies before validation in a fresh worktree. For .NET omit --no-restore until restore succeeds. " +
                "Missing project.assets.json means the check is unverified, not that the source is broken.";
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// The body of the Judge's Spec Conflict section (the criterion and evidence), trimmed to at most 2,000 characters,
        /// or null when the section is absent or empty.
        /// </summary>
        /// <param name="output">Judge agent output.</param>
        /// <returns>The conflict text, or null.</returns>
        public static string? ExtractSpecConflict(string? output)
        {
            if (String.IsNullOrWhiteSpace(output)) return null;
            string body = ExtractSectionBody(output!, SpecConflictSection).Trim();
            if (body.Length == 0) return null;
            return body.Length > 2000 ? body.Substring(0, 2000) + " (truncated)" : body;
        }

        private static string ExtractSectionBody(string output, string sectionName)
        {
            string[] lines = output.Replace("\r\n", "\n").Split('\n');

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string normalized = NormalizeHeadingLine(lines[i], out _);
                if (String.Equals(normalized, sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    start = i + 1;
                    break;
                }
            }
            if (start < 0) return String.Empty;

            List<string> body = new List<string>();
            for (int i = start; i < lines.Length; i++)
            {
                string normalized = NormalizeHeadingLine(lines[i], out bool hasHeadingMarker);
                if (hasHeadingMarker && normalized.Length > 0) break;
                body.Add(lines[i]);
            }
            return String.Join("\n", body).Trim();
        }

        private static string NormalizeHeadingLine(string line, out bool hasHeadingMarker)
        {
            string value = (line ?? String.Empty).Trim();
            hasHeadingMarker = false;
            bool removedTrailingPunctuation = false;

            bool changed;
            do
            {
                changed = false;
                value = value.TrimStart();

                Match heading = Regex.Match(value, @"^#{1,6}\s+");
                if (heading.Success)
                {
                    value = value.Substring(heading.Length);
                    hasHeadingMarker = true;
                    changed = true;
                    continue;
                }

                Match list = Regex.Match(value, @"^(?:[-*]\s+|\d+\.\s+)");
                if (list.Success)
                {
                    value = value.Substring(list.Length);
                    changed = true;
                    continue;
                }

                string? leadingDecoration = GetLeadingDecoration(value);
                if (leadingDecoration != null)
                {
                    value = value.Substring(leadingDecoration.Length);
                    changed = true;
                    continue;
                }

                value = value.TrimEnd();
                string? trailingDecoration = GetTrailingDecoration(value);
                if (trailingDecoration != null)
                {
                    value = value.Substring(0, value.Length - trailingDecoration.Length);
                    changed = true;
                    continue;
                }

                if (!removedTrailingPunctuation
                    && (value.EndsWith(":", StringComparison.Ordinal) || value.EndsWith("-", StringComparison.Ordinal)))
                {
                    value = value.Substring(0, value.Length - 1);
                    removedTrailingPunctuation = true;
                    changed = true;
                }
            }
            while (changed);

            return value.Trim();
        }

        private static string? GetLeadingDecoration(string value)
        {
            foreach (string decoration in new[] { "**", "__", "`", "*", "_" })
            {
                if (value.StartsWith(decoration, StringComparison.Ordinal)) return decoration;
            }
            return null;
        }

        private static string? GetTrailingDecoration(string value)
        {
            foreach (string decoration in new[] { "**", "__", "`", "*", "_" })
            {
                if (value.EndsWith(decoration, StringComparison.Ordinal)) return decoration;
            }
            return null;
        }

        #endregion
    }
}
