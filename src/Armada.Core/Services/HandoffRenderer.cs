namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Renders a <see cref="HandoffArtifact"/> for one reader within a token budget (docs/MODEL_HETEROGENEITY.md, 3.1).
    /// Delivery order: unresolved findings and required actions, check results, diff sections of files named in findings,
    /// other diff sections, the stage output; then always a pointer to the complete artifact through a tool the reader
    /// has and an explicit list of what did not fit. Content is never inlined past the budget. The pointer and the
    /// did-not-fit list are the floor of every rendering: a budget smaller than they are delivers only them.
    /// </summary>
    public static class HandoffRenderer
    {
        #region Public-Members

        /// <summary>
        /// Fraction of a reader's served context given to the handoff (one eighth).
        /// </summary>
        public const int ContextDivisor = 8;

        /// <summary>
        /// Directory, relative to the worktree, that holds handoff files for readers that can only read files.
        /// </summary>
        public const string HandoffDirectory = ".armada/handoff";

        #endregion

        #region Private-Members

        private const int _MaxDidNotFitLines = 25;
        private const int _MinPartialOutputChars = 200;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Estimated tokens of a text (four characters per token, rounded up).
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Estimated tokens.</returns>
        public static int EstimateTokens(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return (text!.Length + 3) / 4;
        }

        /// <summary>
        /// The budget for a reader: one eighth of its served context when known, otherwise the default.
        /// </summary>
        /// <param name="reader">Reader, or null.</param>
        /// <param name="defaultBudgetTokens">Default budget (negative values count as 0).</param>
        /// <returns>Budget in tokens.</returns>
        public static int ResolveBudget(HandoffReader? reader, int defaultBudgetTokens)
        {
            if (reader?.ContextTokens.HasValue == true && reader.ContextTokens.Value > 0) return reader.ContextTokens.Value / ContextDivisor;
            return defaultBudgetTokens < 0 ? 0 : defaultBudgetTokens;
        }

        /// <summary>
        /// How the reader is pointed at the complete artifact: MCP when it has the tools, else a worktree file when it
        /// can read files, else nothing.
        /// </summary>
        /// <param name="reader">Reader, or null (an unknown reader is assumed to have MCP).</param>
        /// <returns>Pointer kind.</returns>
        public static HandoffPointerKindEnum ChoosePointer(HandoffReader? reader)
        {
            if (reader == null || reader.HasMcpTools) return HandoffPointerKindEnum.Mcp;
            if (reader.CanReadFiles) return HandoffPointerKindEnum.File;
            return HandoffPointerKindEnum.None;
        }

        /// <summary>
        /// Worktree-relative path of the handoff file for an artifact (.armada/handoff/&lt;persona&gt;-&lt;mission&gt;.md).
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <returns>Relative path with forward slashes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        public static string FilePathFor(HandoffArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            string persona = new string((artifact.Persona ?? PersonaCatalog.Worker).ToLowerInvariant()
                .Select(c => Char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            string mission = new string(artifact.MissionId.Select(c => Char.IsLetterOrDigit(c) || c == '_' ? c : '-').ToArray());
            return HandoffDirectory + "/" + (persona.Length == 0 ? "stage" : persona) + "-" + mission + ".md";
        }

        /// <summary>
        /// Render an artifact for a reader with a resolved budget.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <param name="reader">Reader, or null for an unknown reader.</param>
        /// <param name="defaultBudgetTokens">Budget used when the reader's served context is not known.</param>
        /// <returns>The rendering.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        public static HandoffRendering Render(HandoffArtifact artifact, HandoffReader? reader, int defaultBudgetTokens)
        {
            return RenderWithBudget(artifact, reader, ResolveBudget(reader, defaultBudgetTokens));
        }

        /// <summary>
        /// Render an artifact for a reader within an explicit budget.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <param name="reader">Reader, or null for an unknown reader.</param>
        /// <param name="budgetTokens">Budget in tokens (negative values count as 0).</param>
        /// <returns>The rendering.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        public static HandoffRendering RenderWithBudget(HandoffArtifact artifact, HandoffReader? reader, int budgetTokens)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            int budget = budgetTokens < 0 ? 0 : budgetTokens;
            HandoffPointerKindEnum pointer = ChoosePointer(reader);
            string? filePath = pointer == HandoffPointerKindEnum.File ? FilePathFor(artifact) : null;

            string header = BuildHeader(artifact, budget);
            string pointerText = BuildPointer(artifact, pointer, filePath);
            List<Unit> units = BuildUnits(artifact);

            List<Unit> included = new List<Unit>();
            List<string> didNotFit = new List<string>();
            // Running estimate: the frame (header, section titles, pointer, list title), then each item and each list line.
            int used = EstimateTokens(Compose(header, included, null, pointerText, didNotFit, false).Evidence) + EstimateTokens(DiffText(included, false)) + 12;
            foreach (Unit unit in units)
            {
                int reserve = EstimateTokens(DidNotFitLine(unit.Label)) + 1;
                int cost = EstimateTokens(unit.Text) + 2;
                if (used + cost + reserve <= budget)
                {
                    included.Add(unit);
                    used += cost;
                    continue;
                }

                Unit? partial = unit.AllowPartial ? Partial(unit, budget - used - reserve - EstimateTokens(DidNotFitLine(unit.Label + ": first 0000000 characters")) - 12) : null;
                if (partial != null)
                {
                    included.Add(partial);
                    didNotFit.Add(partial.OmittedLabel!);
                    used += EstimateTokens(partial.Text) + 2 + EstimateTokens(DidNotFitLine(partial.OmittedLabel!)) + 1;
                }
                else
                {
                    didNotFit.Add(unit.Label);
                    used += reserve;
                }
            }

            // The estimate above reserves room for the list; drop the last included items until the whole rendering fits.
            HandoffRendering rendering = Compose(header, included, units, pointerText, didNotFit, true);
            while (rendering.EstimatedTokens > budget && included.Count > 0)
            {
                Unit last = included[included.Count - 1];
                included.RemoveAt(included.Count - 1);
                if (last.OmittedLabel != null) didNotFit.Remove(last.OmittedLabel);
                didNotFit.Insert(InsertPosition(didNotFit, units, last), last.Source?.Label ?? last.Label);
                rendering = Compose(header, included, units, pointerText, didNotFit, true);
            }

            rendering.BudgetTokens = budget;
            rendering.Pointer = pointer;
            rendering.FilePath = filePath;
            return rendering;
        }

        /// <summary>
        /// The complete artifact as Markdown: the content of the worktree file for readers that can only read files, and
        /// the body an operator reads. Nothing is cut.
        /// </summary>
        /// <param name="artifact">Artifact.</param>
        /// <returns>Markdown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when artifact is null.</exception>
        public static string RenderFullDocument(HandoffArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            StringBuilder doc = new StringBuilder();
            doc.Append("# Handoff artifact ").Append(artifact.Id).Append(" (schema v").Append(artifact.SchemaVersion).Append(")\n\n");
            doc.Append("- Mission: ").Append(artifact.MissionId).Append(" (").Append(artifact.Persona ?? "unknown").Append("), attempt ").Append(artifact.Attempt).Append('\n');
            doc.Append("- Captain: ").Append(artifact.CaptainName ?? artifact.CaptainId ?? "unknown").Append('\n');
            doc.Append("- Verdict: ").Append(artifact.Verdict ?? "none recorded").Append('\n');
            doc.Append("- Branch: ").Append(artifact.BranchName ?? "unknown").Append('\n');
            doc.Append("- Commits: ").Append(artifact.CommitReferences.Count == 0 ? "none recorded" : String.Join(", ", artifact.CommitReferences)).Append("\n\n");

            doc.Append("## Required actions\n").Append(ListOrNone(artifact.RequiredActions)).Append('\n');
            doc.Append("## Findings\n");
            if (artifact.Findings.Count == 0) doc.Append("None recorded.\n");
            foreach (HandoffFinding finding in artifact.Findings) doc.Append(FindingText(finding)).Append('\n');
            doc.Append("\n## Acceptance criteria\n").Append(ListOrNone(artifact.AcceptanceCriteria)).Append('\n');
            doc.Append("## Check results\n").Append(ChecksText(artifact.CheckResults)).Append('\n');
            doc.Append("## Completion lines\n").Append(ListOrNone(artifact.CompletionLines)).Append('\n');
            doc.Append("## Stage output\n");
            if (artifact.Output != null) doc.Append("```text\n").Append(artifact.Output.TrimEnd()).Append("\n```\n\n");
            else doc.Append(artifact.OutputLength == 0 ? "No output recorded.\n\n" : "The output (" + artifact.OutputLength + " characters) was too large to store; read it from mission " + artifact.MissionId + ".\n\n");
            doc.Append("## Diff\n").Append(artifact.DiffPointer ?? "").Append('\n');
            if (artifact.Diff != null) doc.Append("```diff\n").Append(artifact.Diff.TrimEnd()).Append("\n```\n");
            else doc.Append(artifact.DiffLength == 0 ? "No diff recorded.\n" : "The diff (" + artifact.DiffLength + " characters) was too large to store; regenerate it as above.\n");
            return doc.ToString();
        }

        #endregion

        #region Private-Methods

        private static string BuildHeader(HandoffArtifact artifact, int budget)
        {
            StringBuilder header = new StringBuilder();
            header.Append("Handoff artifact ").Append(artifact.Id).Append(" (schema v").Append(artifact.SchemaVersion).Append(") from ")
                .Append(artifact.Persona ?? "unknown").Append(" mission ").Append(artifact.MissionId).Append(", attempt ").Append(artifact.Attempt).Append(". ");
            header.Append("Verdict: ").Append(artifact.Verdict ?? "none recorded").Append(". ");
            header.Append("Commits: ").Append(artifact.CommitReferences.Count == 0 ? "none recorded" : String.Join(", ", artifact.CommitReferences.Take(5))).Append(". ");
            header.Append("Delivered within a budget of ").Append(budget).Append(" tokens.\n");
            return header.ToString();
        }

        private static string BuildPointer(HandoffArtifact artifact, HandoffPointerKindEnum pointer, string? filePath)
        {
            StringBuilder text = new StringBuilder("#### Complete artifact\n");
            switch (pointer)
            {
                case HandoffPointerKindEnum.Mcp:
                    text.Append("Call the MCP tool get_handoff_artifact with artifactId ").Append(artifact.Id)
                        .Append(" (or list_handoff_artifacts with missionId ").Append(artifact.MissionId).Append(") for every finding, check, the full output and the full diff.\n");
                    break;
                case HandoffPointerKindEnum.File:
                    text.Append("Read the file ").Append(filePath).Append(" in your worktree for every finding, check, the full output and the full diff. It is not part of the work; do not commit it.\n");
                    break;
                default:
                    text.Append("You have neither MCP tools nor file access, so the complete artifact cannot reach you. Work from what is above; ")
                        .Append("if an item under Did not fit matters, say so in your result instead of guessing. (Operators: GET /api/v1/handoff-artifacts/").Append(artifact.Id).Append(".)\n");
                    break;
            }

            if (!String.IsNullOrEmpty(artifact.DiffPointer)) text.Append("Full diff: ").Append(artifact.DiffPointer).Append('\n');
            return text.ToString();
        }

        private static List<Unit> BuildUnits(HandoffArtifact artifact)
        {
            List<Unit> units = new List<Unit>();
            if (artifact.RequiredActions.Count > 0)
                units.Add(new Unit("required actions", "#### Required actions\n" + ListOrNone(artifact.RequiredActions), false));

            IEnumerable<HandoffFinding> open = artifact.Findings.Where(f => !f.Resolved)
                .OrderBy(f => f.Severity == HandoffFindingSeverityEnum.Blocking ? 0 : 1).ThenBy(f => f.Number);
            foreach (HandoffFinding finding in open)
                units.Add(new Unit("finding " + finding.Number + (finding.File != null ? " (" + finding.File + ")" : ""), FindingText(finding) + "\n", false) { Finding = finding });

            if (artifact.CheckResults.Count > 0)
                units.Add(new Unit("check results", "#### Check results\n" + ChecksText(artifact.CheckResults), false));

            List<DiffSection> sections = SplitDiff(artifact.Diff);
            HashSet<string> named = new HashSet<string>(artifact.Findings.Where(f => f.File != null).Select(f => NormalizePath(f.File!)), StringComparer.OrdinalIgnoreCase);
            foreach (DiffSection section in sections.Where(s => IsNamed(s.Path, named)))
                units.Add(new Unit("diff of " + section.Path + " (named in a finding)", section.Text, false) { IsDiff = true });
            foreach (DiffSection section in sections.Where(s => !IsNamed(s.Path, named)))
                units.Add(new Unit("diff of " + section.Path, section.Text, false) { IsDiff = true });

            if (!String.IsNullOrWhiteSpace(artifact.Output))
                units.Add(new Unit("stage output (" + artifact.OutputLength + " characters)", artifact.Output!.Trim(), true));
            return units;
        }

        private static HandoffRendering Compose(string header, List<Unit> included, List<Unit>? all, string pointerText, List<string> didNotFit, bool final)
        {
            // The evidence, and the same evidence without the findings section, for a reader (rework) that lists the
            // delivered findings elsewhere in its prompt.
            StringBuilder evidence = new StringBuilder(header);
            StringBuilder withoutFindings = new StringBuilder(header);
            StringBuilder findings = new StringBuilder();
            List<Unit> evidenceUnits = included.Where(u => !u.IsDiff).ToList();
            bool anyFinding = evidenceUnits.Any(u => u.Label.StartsWith("finding ", StringComparison.Ordinal));
            if (anyFinding) evidence.Append("#### Unresolved findings\n");
            foreach (Unit unit in evidenceUnits)
            {
                string text = unit.Text.TrimEnd() + "\n";
                if (unit.Label.StartsWith("stage output", StringComparison.Ordinal))
                {
                    evidence.Append("#### Stage output\n");
                    withoutFindings.Append("#### Stage output\n");
                }

                evidence.Append(text);
                if (unit.Finding != null) findings.Append(text);
                else withoutFindings.Append(text);
            }

            StringBuilder tail = new StringBuilder();
            if (final && all != null && all.Count == 0) tail.Append("No findings, checks, output or diff were recorded for this stage.\n");
            tail.Append(pointerText);
            tail.Append("#### Did not fit\n");
            if (didNotFit.Count == 0)
            {
                tail.Append("Nothing was left out.\n");
            }
            else
            {
                foreach (string item in didNotFit.Take(_MaxDidNotFitLines)) tail.Append(DidNotFitLine(item));
                if (didNotFit.Count > _MaxDidNotFitLines) tail.Append("- and ").Append(didNotFit.Count - _MaxDidNotFitLines).Append(" more items\n");
            }

            evidence.Append(tail);
            withoutFindings.Append(tail);

            HandoffRendering rendering = new HandoffRendering();
            rendering.Evidence = evidence.ToString();
            rendering.EvidenceWithoutFindings = withoutFindings.ToString();
            rendering.FindingsText = findings.ToString();
            rendering.Findings = evidenceUnits.Where(u => u.Finding != null).Select(u => u.Finding!).ToList();
            rendering.Diff = DiffText(included, final);
            // One entry per unit left out (diff sections grow with the diff): keep the first MaxConfigItems, in delivery
            // order (findings before diffs), and count every one.
            rendering.DidNotFitCount = didNotFit.Count;
            rendering.DidNotFit = didNotFit.Take(CollectionLimits.MaxConfigItems).ToList();
            rendering.EstimatedTokens = EstimateTokens(rendering.Evidence) + EstimateTokens(rendering.Diff);
            return rendering;
        }

        private static string DiffText(List<Unit> included, bool final)
        {
            List<Unit> diffs = included.Where(u => u.IsDiff).ToList();
            if (diffs.Count == 0) return "No diff sections fit the handoff budget or none were recorded; see the complete artifact pointer and inspect git diff on the branch.";
            StringBuilder text = new StringBuilder();
            foreach (Unit unit in diffs) text.Append(unit.Text.TrimEnd()).Append('\n');
            return text.ToString();
        }

        private static Unit? Partial(Unit unit, int availableTokens)
        {
            int chars = availableTokens * 4;
            if (chars < _MinPartialOutputChars || chars >= unit.Text.Length) return null;
            string tail = unit.Text.Substring(unit.Text.Length - chars);
            string note = "[last " + chars + " of " + unit.Text.Length + " characters]\n";
            Unit partial = new Unit(unit.Label, note + tail, false);
            partial.Source = unit;
            partial.OmittedLabel = unit.Label + ": first " + (unit.Text.Length - chars) + " characters";
            return partial;
        }

        private static int InsertPosition(List<string> didNotFit, List<Unit> units, Unit removed)
        {
            int order = units.IndexOf(removed.Source ?? removed);
            for (int i = 0; i < didNotFit.Count; i++)
            {
                int other = units.FindIndex(u => didNotFit[i] == u.Label || didNotFit[i].StartsWith(u.Label + ":", StringComparison.Ordinal));
                if (other > order) return i;
            }

            return didNotFit.Count;
        }

        private static string DidNotFitLine(string label)
        {
            return "- " + label + "\n";
        }

        private static string FindingText(HandoffFinding finding)
        {
            StringBuilder text = new StringBuilder();
            text.Append(finding.Number).Append(". [").Append(finding.Severity).Append("] ");
            if (finding.File != null) text.Append(finding.File).Append(finding.Line.HasValue ? ":" + finding.Line.Value : "").Append(" -- ");
            text.Append("Required fix: ").Append(finding.RequiredFix ?? "see the finding").Append('\n');
            foreach (string line in finding.Summary.Replace("\r\n", "\n").Split('\n')) text.Append("   ").Append(line).Append('\n');
            return text.ToString();
        }

        private static string ChecksText(List<StageCheckResult> checks)
        {
            if (checks.Count == 0) return "No check result recorded (the work was not mechanically verified).\n";
            StringBuilder text = new StringBuilder();
            foreach (StageCheckResult check in checks)
            {
                text.Append("- ").Append(check.Status);
                if (check.Reason != StageCheckReasonEnum.None) text.Append(" (").Append(check.Reason).Append(')');
                if (check.Commands.Count > 0) text.Append(": ").Append(String.Join("; ", check.Commands));
                if (check.FailedCommand != null) text.Append(". Failed: ").Append(check.FailedCommand).Append(check.ExitCode.HasValue ? " (exit " + check.ExitCode.Value + ")" : "");
                if (check.PreExistingFailures.Count > 0) text.Append(". Failing before the stage too: ").Append(String.Join("; ", check.PreExistingFailures));
                text.Append('\n');
            }

            return text.ToString();
        }

        private static string ListOrNone(List<string> items)
        {
            if (items.Count == 0) return "None recorded.\n";
            StringBuilder text = new StringBuilder();
            foreach (string item in items) text.Append("- ").Append(item).Append('\n');
            return text.ToString();
        }

        private static List<DiffSection> SplitDiff(string? diff)
        {
            List<DiffSection> sections = new List<DiffSection>();
            if (String.IsNullOrWhiteSpace(diff)) return sections;
            string[] lines = diff!.Replace("\r\n", "\n").Split('\n');
            DiffSection? current = null;
            StringBuilder? text = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) || current == null)
                {
                    if (current != null) { current.Text = text!.ToString(); sections.Add(current); }
                    current = new DiffSection { Path = DiffPath(line) };
                    text = new StringBuilder();
                }

                text!.Append(line).Append('\n');
            }

            if (current != null) { current.Text = text!.ToString(); sections.Add(current); }
            return sections;
        }

        private static string DiffPath(string line)
        {
            if (!line.StartsWith("diff --git ", StringComparison.Ordinal)) return "(diff preamble)";
            int b = line.LastIndexOf(" b/", StringComparison.Ordinal);
            return b >= 0 ? line.Substring(b + 3).Trim() : line.Substring("diff --git ".Length).Trim();
        }

        private static string NormalizePath(string path)
        {
            string p = path.Replace('\\', '/').Trim('`', ' ');
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            return p;
        }

        private static bool IsNamed(string path, HashSet<string> named)
        {
            string normalized = NormalizePath(path);
            if (named.Contains(normalized)) return true;
            foreach (string name in named)
                if (normalized.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        #endregion

        #region Private-Classes

        private sealed class Unit
        {
            public string Label { get; }

            public string Text { get; }

            public bool AllowPartial { get; }

            public bool IsDiff { get; set; } = false;

            public Unit? Source { get; set; } = null;

            public HandoffFinding? Finding { get; set; } = null;

            public string? OmittedLabel { get; set; } = null;

            public Unit(string label, string text, bool allowPartial)
            {
                Label = label;
                Text = text;
                AllowPartial = allowPartial;
            }
        }

        private sealed class DiffSection
        {
            public string Path { get; set; } = "";

            public string Text { get; set; } = "";
        }

        #endregion
    }
}
