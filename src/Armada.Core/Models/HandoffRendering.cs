namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A handoff artifact rendered for one reader within a token budget: the evidence block (findings, required actions,
    /// checks, output, pointer, did-not-fit list) and the diff block (relevant diff sections).
    /// </summary>
    public class HandoffRendering
    {
        #region Public-Members

        /// <summary>
        /// The evidence block: unresolved findings and required actions first, then checks and stage output, then the
        /// pointer to the complete artifact and the explicit list of what did not fit.
        /// </summary>
        public string Evidence { get; set; } = "";

        /// <summary>
        /// The relevant diff sections that fit (files named in findings first), or a note that none fit.
        /// </summary>
        public string Diff { get; set; } = "";

        /// <summary>
        /// The token budget the rendering was given.
        /// </summary>
        public int BudgetTokens { get; set; } = 0;

        /// <summary>
        /// Estimated tokens of <see cref="Evidence"/> plus <see cref="Diff"/>.
        /// </summary>
        public int EstimatedTokens { get; set; } = 0;

        /// <summary>
        /// How the reader is pointed at the complete artifact.
        /// </summary>
        public HandoffPointerKindEnum Pointer { get; set; } = HandoffPointerKindEnum.Mcp;

        /// <summary>
        /// Worktree-relative path of the artifact file when <see cref="Pointer"/> is File, else null.
        /// </summary>
        public string? FilePath { get; set; } = null;

        /// <summary>
        /// Every item that did not fit the budget, including any beyond <see cref="DidNotFit"/>.
        /// </summary>
        public int DidNotFitCount { get; set; } = 0;

        /// <summary>
        /// The items that did not fit the budget, in delivery order: the first
        /// <see cref="CollectionLimits.MaxConfigItems"/>; <see cref="DidNotFitCount"/> is the full count.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "First MaxConfigItems items; DidNotFitCount is the total")]
        public List<string> DidNotFit { get; set; } = new List<string>();

        /// <summary>
        /// The findings delivered in <see cref="Evidence"/>, in delivery order; findings that did not fit are listed in
        /// <see cref="DidNotFit"/> instead.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "A subset of HandoffArtifact.Findings")]
        public List<HandoffFinding> Findings { get; set; } = new List<HandoffFinding>();

        /// <summary>
        /// The delivered findings exactly as they appear in <see cref="Evidence"/> (one numbered block per finding, with
        /// severity, file, line and required fix), without the section title. Empty when no finding was delivered.
        /// </summary>
        public string FindingsText { get; set; } = "";

        /// <summary>
        /// <see cref="Evidence"/> without its findings section, for a prompt that lists <see cref="FindingsText"/>
        /// elsewhere (the rework prompt), so the findings are delivered once and the budget still holds.
        /// </summary>
        public string EvidenceWithoutFindings { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HandoffRendering()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The evidence and diff blocks as one text, for a prompt with a single place for prior-stage context.
        /// </summary>
        /// <returns>Combined text.</returns>
        public string Combined()
        {
            return Evidence.TrimEnd() + "\n\n" + Diff.TrimEnd() + "\n";
        }

        #endregion
    }
}
