namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Safe, deterministic repair of stage reports whose work was acceptable but whose output broke the contract
    /// (MODEL_HETEROGENEITY.md, design principle 4). Every check here is pure: it reads the output and the repository
    /// evidence it is given and never invents a verdict, a plan or work. Anything ambiguous is refused, and the caller
    /// keeps its existing failure path.
    /// </summary>
    public static class ContractRepair
    {
        #region Public-Members

        /// <summary>
        /// Event type of the record written for each repair; its payload is a <see cref="ContractRepairRecord"/>.
        /// </summary>
        public const string EventType = "mission.contract_repaired";

        /// <summary>
        /// Start of the comment line Armada writes above a canonical line it appends to repaired output.
        /// </summary>
        public const string AppendedMarkerPrefix = "<!-- armada:contract-repair ";

        #endregion

        #region Private-Members

        private static readonly Regex _ResultLike = new Regex(
            @"ARMADA[ \t]*[:_\-]?[ \t]*RESULT",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _NearbyVerdict = new Regex(
            @"^\[?[ \t]*ARMADA[ \t]*[:_\- ][ \t]*VERDICT[ \t]*\]?[ \t]*[:=\-]?[ \t]*[*_`]*[ \t]*(?<verdict>PASS|FAIL|NEEDS[ _\-]?REVISION|SPEC[ _\-]?CONFLICT)[*_`]*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A verdict without the ARMADA token, read only inside a "Verdict" section: "Verdict: NEEDS_REVISION" or a bare word.
        private static readonly Regex _ProseVerdict = new Regex(
            @"^(?:(?:final[ \t]+)?verdict[ \t]*[:=\-]?[ \t]*)?[*_`]*[ \t]*(?<verdict>PASS|FAIL|NEEDS[ _\-]?REVISION|SPEC[ _\-]?CONFLICT)[*_`]*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _VerdictHeading = new Regex(
            @"^(?:final[ \t]+)?verdict$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _VerdictHeadingWithValue = new Regex(
            @"^(?:final[ \t]+)?verdict[ \t]*[:=\-]",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly char[] _LeadingDecoration = new char[] { ' ', '\t', '*', '`', '>', '#', '-' };

        private static readonly char[] _TrailingDecoration = new char[] { ' ', '\t', '*', '`', '.', '!' };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify a work stage's completion line.
        /// </summary>
        /// <param name="output">The stage's output.</param>
        /// <returns>
        /// <see cref="ContractRepairKindEnum.None"/> when the canonical completion line is present,
        /// <see cref="ContractRepairKindEnum.CompletionLineMalformed"/> when a line names the result marker in another form,
        /// otherwise <see cref="ContractRepairKindEnum.CompletionLineMissing"/>.
        /// </returns>
        public static ContractRepairKindEnum ClassifyCompletionLine(string? output)
        {
            if (String.IsNullOrEmpty(output)) return ContractRepairKindEnum.CompletionLineMissing;
            if (output.IndexOf(NoOpCompletionDetector.CompletionMarker, StringComparison.Ordinal) >= 0) return ContractRepairKindEnum.None;
            return _ResultLike.IsMatch(output) ? ContractRepairKindEnum.CompletionLineMalformed : ContractRepairKindEnum.CompletionLineMissing;
        }

        /// <summary>
        /// Whether the repository shows that a stage produced work: a non-empty diff, or a dock head that moved beyond the
        /// commit the dock started from. Uncommitted non-scratch changes count too, because the leftover-work commit runs first
        /// and turns them into a commit.
        /// </summary>
        /// <param name="diffSnapshot">Captured diff, or null.</param>
        /// <param name="startCommit">Commit the dock started from, or null when unknown.</param>
        /// <param name="headCommit">Commit the dock ended on, or null when unknown.</param>
        /// <returns>True only on positive evidence of new work; unknown commits are not evidence.</returns>
        public static bool HasNewWork(string? diffSnapshot, string? startCommit, string? headCommit)
        {
            if (!String.IsNullOrWhiteSpace(diffSnapshot)) return true;
            if (String.IsNullOrWhiteSpace(startCommit) || String.IsNullOrWhiteSpace(headCommit)) return false;
            return !String.Equals(startCommit.Trim(), headCommit.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Recover a Judge verdict written in a nearby non-canonical form, such as <c>**[ARMADA:VERDICT] PASS**</c>,
        /// <c>[ARMADA:VERDICT]: FAIL</c> or <c>ARMADA VERDICT: NEEDS REVISION.</c>, or in prose inside a section headed
        /// "Verdict" (<c>## Verdict</c> then <c>**Verdict: NEEDS_REVISION**</c> or a bare <c>FAIL</c>, or a heading such as
        /// <c>### Verdict: FAIL</c>). A prose PASS is never recovered, because only a line naming the ARMADA verdict marker
        /// may permit landing; it goes to the format repair turn instead. Refused when any canonical verdict line exists (the canonical parser decides, including
        /// conflicts), when there is not exactly one candidate line, or when the candidate is a SPEC_CONFLICT without a
        /// non-empty <c>## Spec Conflict</c> section quoting the criterion. Fenced lines are ignored, as they are by the
        /// canonical parser, and fence state resets at the output truncation marker.
        /// </summary>
        /// <param name="output">Judge output.</param>
        /// <param name="verdict">The recovered PASS, FAIL, NEEDS_REVISION, or SPEC_CONFLICT verdict; None when refused.</param>
        /// <param name="sourceLine">The trimmed line the verdict was read from; null when refused.</param>
        /// <returns>True when exactly one unambiguous verdict was recovered.</returns>
        public static bool TryRecoverVerdict(string? output, out JudgeVerdictEnum verdict, out string? sourceLine)
        {
            verdict = JudgeVerdictEnum.None;
            sourceLine = null;
            if (String.IsNullOrEmpty(output)) return false;

            List<string> candidates = new List<string>();
            JudgeVerdictEnum candidateVerdict = JudgeVerdictEnum.None;
            bool inFence = false;
            bool inVerdictSection = false;
            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
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
                if (JudgeVerdictParser.ParseLine(line).HasValue) return false;

                string stripped = line.TrimStart(_LeadingDecoration).TrimEnd(_TrailingDecoration);
                Match match = _NearbyVerdict.Match(stripped);
                bool proseMatch = !match.Success;
                if (!match.Success && line.StartsWith("#", StringComparison.Ordinal))
                {
                    // A heading opens a new section; "## Verdict" opens the one section where prose verdicts are read,
                    // and a heading that states the verdict itself ("### Verdict: PASS") is read as one.
                    inVerdictSection = _VerdictHeading.IsMatch(stripped);
                    if (inVerdictSection || !_VerdictHeadingWithValue.IsMatch(stripped)) continue;
                    match = _ProseVerdict.Match(stripped);
                }
                else if (!match.Success && inVerdictSection)
                {
                    match = _ProseVerdict.Match(stripped);
                }

                if (!match.Success) continue;

                candidates.Add(line);
                candidateVerdict = ToVerdict(match.Groups["verdict"].Value);

                // Only a line naming the ARMADA verdict marker can permit landing; a prose PASS goes to the format repair turn.
                if (proseMatch && candidateVerdict == JudgeVerdictEnum.Pass) return false;
            }

            if (candidates.Count != 1 || candidateVerdict == JudgeVerdictEnum.None) return false;

            // A spec conflict sends the stage to a person, so it is only recovered with its quoted criterion and evidence.
            if (candidateVerdict == JudgeVerdictEnum.SpecConflict)
            {
                // The verdict line itself does not count as the quoted criterion.
                string withoutVerdict = String.Join("\n", output.Replace("\r\n", "\n").Split('\n').Where(l => !String.Equals(l.Trim(), candidates[0], StringComparison.Ordinal)));
                if (JudgeContract.ExtractSpecConflict(withoutVerdict) == null) return false;
            }
            verdict = candidateVerdict;
            sourceLine = candidates[0];
            return true;
        }

        /// <summary>
        /// The canonical verdict line for a PASS, FAIL, NEEDS_REVISION, or SPEC_CONFLICT verdict.
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>The canonical line.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for any other verdict.</exception>
        public static string CanonicalVerdictLine(JudgeVerdictEnum verdict)
        {
            return StageOutputContract.VerdictLine(verdict);
        }

        /// <summary>
        /// Recover an Architect plan that is valid but not in a closed <c>armada-plan</c> fence: a single fenced block with
        /// another info string (<c>json</c>, none, a misspelled fence name, a backtick or tilde fence), a plan object
        /// with prose around it inside a fence, bare JSON with no fence, or an <c>armada-plan</c> fence left open at the
        /// end. Refused when any closed canonical plan block exists (the canonical parser decides), when more than one
        /// plan-like object appears anywhere in the output (a draft and a final plan are never told apart), or when the
        /// single candidate does not pass the plan contract.
        /// </summary>
        /// <param name="output">Architect output.</param>
        /// <param name="planJson">The plan JSON to wrap in a canonical fence; null when refused.</param>
        /// <returns>True when exactly one valid plan was recovered.</returns>
        public static bool TryRecoverArchitectPlan(string? output, out string? planJson)
        {
            planJson = null;
            if (String.IsNullOrWhiteSpace(output)) return false;

            List<string> planLike = new List<string>();
            StringBuilder outside = new StringBuilder();
            string? openFence = null;
            string? openInfo = null;
            StringBuilder? current = null;
            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (current == null)
                {
                    if (ProtocolLines.IsFence(line))
                    {
                        openFence = line.Substring(0, 3);
                        openInfo = line.Substring(3).Trim();
                        current = new StringBuilder();
                    }
                    else
                    {
                        outside.AppendLine(rawLine);
                    }

                    continue;
                }

                bool truncated = ProtocolLines.IsTruncationMarker(line);
                if (truncated || line.StartsWith(openFence!, StringComparison.Ordinal))
                {
                    if (!truncated && String.Equals(openInfo, ArchitectPlanParser.FenceInfo, StringComparison.OrdinalIgnoreCase)) return false;
                    planLike.AddRange(PlanObjects(current.ToString()));
                    current = null;
                    openFence = null;
                    openInfo = null;
                    continue;
                }

                current.AppendLine(rawLine);
            }

            if (current != null) planLike.AddRange(PlanObjects(current.ToString()));
            planLike.AddRange(PlanObjects(outside.ToString()));
            if (planLike.Count != 1) return false;

            string json = planLike[0].Trim();
            string canonical = "```" + ArchitectPlanParser.FenceInfo + "\n" + json + "\n```";
            ArchitectPlanParseResult result = ArchitectPlanParser.Parse(canonical, Constants.DefaultMaxHierarchyDepth);
            if (result.Status != ArchitectPlanParseStatusEnum.Parsed) return false;

            planJson = json;
            return true;
        }

        /// <summary>
        /// Append a canonical line or block to a stage's output, below a marker naming the repair, so the original output is
        /// kept unchanged above it and every later reader (handoff, recovery, stage outcome) parses the same repaired result.
        /// A code fence the original left open is closed first so the appended text is not read as part of it.
        /// </summary>
        /// <param name="output">Original output.</param>
        /// <param name="kind">Repair kind.</param>
        /// <param name="canonical">Canonical text to append.</param>
        /// <returns>The repaired output.</returns>
        /// <exception cref="ArgumentNullException">Thrown when canonical is null.</exception>
        public static string AppendRepair(string? output, ContractRepairKindEnum kind, string canonical)
        {
            if (canonical == null) throw new ArgumentNullException(nameof(canonical));
            string original = output ?? "";
            string separator = original.Length == 0 || original.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "\n\n";
            if (HasOpenFence(original)) separator = (original.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + "```\n\n";
            return original + separator + AppendedMarkerPrefix + kind + ": appended by Armada; the stage's original output is above -->\n" + canonical + "\n";
        }

        #endregion

        #region Private-Methods

        private static JudgeVerdictEnum ToVerdict(string word)
        {
            return word.ToUpperInvariant().Replace(" ", "").Replace("_", "").Replace("-", "") switch
            {
                "PASS" => JudgeVerdictEnum.Pass,
                "FAIL" => JudgeVerdictEnum.Fail,
                "NEEDSREVISION" => JudgeVerdictEnum.NeedsRevision,
                "SPECCONFLICT" => JudgeVerdictEnum.SpecConflict,
                _ => JudgeVerdictEnum.None
            };
        }

        private static bool HasOpenFence(string output)
        {
            bool open = false;
            foreach (string rawLine in output.Replace("\r\n", "\n").Split('\n'))
            {
                if (ProtocolLines.IsTruncationMarker(rawLine)) open = false;
                else if (rawLine.Trim().StartsWith("```", StringComparison.Ordinal)) open = !open;
            }

            return open;
        }

        // Every top-level object that names "missions" counts, valid or not, so a broken draft next to a final plan
        // refuses the repair instead of silently choosing one (R6).
        private static List<string> PlanObjects(string text)
        {
            List<string> plans = new List<string>();
            foreach (string candidate in EmbeddedJsonExtractor.FindObjects(text))
                if (candidate.IndexOf("\"missions\"", StringComparison.OrdinalIgnoreCase) >= 0) plans.Add(candidate);
            return plans;
        }

        #endregion
    }
}
