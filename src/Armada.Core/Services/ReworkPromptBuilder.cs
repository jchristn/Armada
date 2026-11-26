namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the rework prompt of a Worker that a Judge or verifier sent back (docs/MODEL_HETEROGENEITY.md, Phase 3 item 2):
    /// numbered findings from the reviewer's handoff artifact, the rework demands as a numbered list with one demand per
    /// line, and the output contract repeated at the end. The section is persisted on the rework mission's description
    /// when the rework is scheduled; at launch <see cref="Compose"/> replaces its findings placeholder with the findings
    /// the <see cref="HandoffRenderer"/> delivered within the reader's budget and moves the output contract to the end.
    /// </summary>
    public static class ReworkPromptBuilder
    {
        #region Public-Members

        /// <summary>
        /// Start of the rework section in a rework mission's description (followed by the round number).
        /// </summary>
        public const string SectionPrefix = "## Rework round ";

        /// <summary>
        /// Line replaced at launch with the numbered findings of the reviewer's handoff artifact.
        /// </summary>
        public const string FindingsPlaceholder = "<!-- armada:rework-findings -->";

        /// <summary>
        /// The required completion line of a rework Worker, stated at the end of the rework prompt.
        /// </summary>
        public const string OutputContract = StageOutputContract.CompletionSentence;

        /// <summary>
        /// The rework demands, one per line, in the order they are numbered in the prompt.
        /// </summary>
        public static IReadOnlyList<string> Demands { get; } = new List<string>
        {
            "Resolve every reported finding.",
            "Check adjacent paths of the same feature against the original mission acceptance requirements.",
            "Reconcile findings from all preceding verifiers, including those labelled nonblocking.",
            "Treat a reproducible violation of an explicit acceptance criterion or invariant as blocking, whatever its label.",
            "Fix each unresolved finding with a regression test, or explain with evidence why it is outside this slice contract.",
            "Treat the findings as required corrections, not as a replacement for the original scope.",
            "Do not treat the findings as proof that unreported paths work.",
            "Keep everything that is already right.",
            "Add regression coverage that exercises the affected behavior.",
            "Run the relevant tests.",
            "Commit your changes.",
            "Report every original requirement that remains unverified, including publication and delivery.",
            "Do not start over.",
            "Do not expand the mission scope.",
            "End with the completion line in the output contract below."
        };

        #endregion

        #region Private-Members

        private const string _FindingsFallback = "The reviewer's findings are in the prior stage evidence below. Treat each problem it reports as one finding.";
        private const string _NoStructuredFindings = "The review has no structured findings. Read it in the prior stage evidence below. Treat each problem it reports as one finding.";
        private const string _ContractHeading = "### Output contract\n";
        private const string _OutsideEvidenceNote = "The reviewer's findings are listed in the rework section above; the rest of its handoff artifact follows.\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The rework section persisted on a rework mission's description: heading, findings placeholder with a fallback
        /// line, numbered demands and the output contract.
        /// </summary>
        /// <param name="round">Rework round (1-based).</param>
        /// <param name="reviewerPersona">Persona of the stage that requested rework (Judge or a verifier), or null.</param>
        /// <returns>The section, starting with a blank line.</returns>
        public static string BuildSection(int round, string? reviewerPersona)
        {
            string reviewer = String.IsNullOrWhiteSpace(reviewerPersona) ? PersonaCatalog.Judge : reviewerPersona!.Trim();
            StringBuilder text = new StringBuilder();
            text.Append("\n\n").Append(SectionPrefix).Append(round).Append(" requested by the ").Append(reviewer).Append('\n');
            text.Append("A reviewer checked this branch and asked for changes.\n");
            text.Append("Its review is the previous stage output below.\n\n");
            text.Append("### Findings to fix\n");
            text.Append(FindingsPlaceholder).Append('\n');
            text.Append(_FindingsFallback).Append('\n');
            text.Append('\n').Append(BuildDemands());
            text.Append('\n').Append(_ContractHeading).Append(OutputContract).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// The rework demands as a numbered list, one demand per line.
        /// </summary>
        /// <returns>The demands block with its heading.</returns>
        public static string BuildDemands()
        {
            StringBuilder text = new StringBuilder("### Rework demands\n");
            for (int i = 0; i < Demands.Count; i++) text.Append(i + 1).Append(". ").Append(Demands[i]).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// The numbered findings block for a rendering: the findings the renderer delivered within its budget, a note
        /// when some or all did not fit (they stay in the did-not-fit list), or the fallback when the review has no
        /// structured findings.
        /// </summary>
        /// <param name="rendering">The reviewer's handoff rendered for this reader, or null when there is none.</param>
        /// <returns>Findings text.</returns>
        public static string BuildFindings(HandoffRendering? rendering)
        {
            if (rendering == null) return _FindingsFallback + "\n";
            bool findingsLeftOut = rendering.DidNotFit.Any(d => d.StartsWith("finding ", StringComparison.Ordinal));
            if (rendering.Findings.Count == 0)
            {
                return findingsLeftOut
                    ? "No finding fit the handoff budget. They are listed under Did not fit below. Read them through the complete artifact pointer.\n"
                    : _NoStructuredFindings + "\n";
            }

            string text = rendering.FindingsText.TrimEnd() + "\n";
            if (findingsLeftOut) text += "More findings did not fit the handoff budget. They are listed under Did not fit below. Read them through the complete artifact pointer.\n";
            return text;
        }

        /// <summary>
        /// The description without its rework section (from the rework heading to the end), so a later rework round or a
        /// repeated verification stage starts from the task. A description without a rework heading is returned unchanged.
        /// </summary>
        /// <param name="description">Description, or null.</param>
        /// <returns>The description up to its rework section.</returns>
        public static string StripSection(string? description)
        {
            string text = description ?? String.Empty;
            int cut = text.IndexOf("\n\n" + SectionPrefix, StringComparison.Ordinal);
            return cut > 0 ? text.Substring(0, cut).TrimEnd() : text;
        }

        /// <summary>
        /// Whether a description carries a rework section built by <see cref="BuildSection"/>.
        /// </summary>
        /// <param name="description">Description, or null.</param>
        /// <returns>True when the findings placeholder is present.</returns>
        public static bool IsReworkDescription(string? description)
        {
            return description != null && description.Contains(FindingsPlaceholder, StringComparison.Ordinal);
        }

        /// <summary>
        /// Complete a rework description at launch: the findings placeholder becomes the numbered findings of the
        /// rendering, the prior-stage placeholder receives the rest of the handoff (evidence without the findings, then the
        /// diff), and the output contract is repeated at the end. Without a rendering the fallback line stays and only the
        /// placeholder line is removed. A description without a rework section is returned unchanged.
        /// </summary>
        /// <param name="description">The rework mission's description, or null.</param>
        /// <param name="rendering">The reviewer's handoff rendered for this reader, or null when there is none.</param>
        /// <param name="evidenceElsewhere">Text for the prior-stage placeholder when the persona template already carries
        /// the evidence, or null to deliver the evidence in the description.</param>
        /// <returns>The completed description.</returns>
        public static string Compose(string? description, HandoffRendering? rendering, string? evidenceElsewhere = null)
        {
            string text = description ?? String.Empty;
            if (!IsReworkDescription(text)) return text;

            text = ReplaceFindings(text, BuildFindings(rendering), rendering != null);
            if (rendering != null)
            {
                string block;
                if (evidenceElsewhere != null) block = evidenceElsewhere;
                else if (rendering.Findings.Count > 0) block = _OutsideEvidenceNote + rendering.EvidenceWithoutFindings.TrimEnd() + "\n\n" + rendering.Diff.TrimEnd() + "\n";
                else block = rendering.Combined();
                text = PipelineHandoffContext.Fill(text, block);
            }

            // The output contract closes the rework prompt, after the prior-stage evidence appended at handoff.
            string trimmed = text.TrimEnd();
            if (!trimmed.EndsWith(OutputContract, StringComparison.Ordinal))
                text = trimmed + "\n\n" + _ContractHeading + OutputContract + "\n";
            return text;
        }

        #endregion

        #region Private-Methods

        private static string ReplaceFindings(string text, string findings, bool replaceFallback)
        {
            int at = text.IndexOf(FindingsPlaceholder, StringComparison.Ordinal);
            int lineEnd = text.IndexOf('\n', at);
            int end = lineEnd < 0 ? text.Length : lineEnd + 1;
            if (replaceFallback && text.Substring(end).StartsWith(_FindingsFallback + "\n", StringComparison.Ordinal))
                end += _FindingsFallback.Length + 1;
            return text.Substring(0, at) + (replaceFallback ? findings : String.Empty) + text.Substring(end);
        }

        #endregion
    }
}
