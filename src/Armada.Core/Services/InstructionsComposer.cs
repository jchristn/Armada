namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// Assembles a mission instructions file from declared sections and holds it to the reader's budget (RS-6.11). The
    /// budget is a quarter of the reader's served context, less the tool schemas the launch sends; when the context is
    /// unknown nothing is trimmed. Over budget, sections are replaced by a one-line note in a fixed order, highest
    /// <see cref="InstructionSection.TrimRank"/> first: papercuts, model-context updates, skills, validation notes, git
    /// anchors, the repository's own instructions, model context, style guide, project context, then playbooks. The
    /// role and mission description (with its acceptance criteria and the handoff, which has its own budget), the mission
    /// mode, the rules, the progress signals and the output contract are never trimmed.
    /// </summary>
    public static class InstructionsComposer
    {
        #region Public-Members

        /// <summary>
        /// The instructions file may use the reader's served context divided by this.
        /// </summary>
        public const int ContextDivisor = 4;

        /// <summary>
        /// The smallest budget a known context yields, so tool schemas never leave a budget of zero.
        /// </summary>
        public const long MinimumBudgetTokens = 2048;

        /// <summary>Trim rank of the papercut directive (trimmed first).</summary>
        public const int PapercutsRank = 10;

        /// <summary>Trim rank of the model-context update instructions.</summary>
        public const int ModelContextUpdatesRank = 9;

        /// <summary>Trim rank of the project-profile skills.</summary>
        public const int SkillsRank = 8;

        /// <summary>Trim rank of the workflow profile's validation notes.</summary>
        public const int ValidationNotesRank = 7;

        /// <summary>Trim rank of the resolved git anchors.</summary>
        public const int GitAnchorsRank = 6;

        /// <summary>Trim rank of the repository's own instructions file.</summary>
        public const int ExistingInstructionsRank = 5;

        /// <summary>Trim rank of the vessel model context.</summary>
        public const int ModelContextRank = 4;

        /// <summary>Trim rank of the vessel style guide.</summary>
        public const int StyleGuideRank = 3;

        /// <summary>Trim rank of the vessel project context.</summary>
        public const int ProjectContextRank = 2;

        /// <summary>Trim rank of the selected playbooks (trimmed last).</summary>
        public const int PlaybooksRank = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Estimated tokens of a text, with the estimate the API runtime uses (<see cref="CharacterRatioTokenEstimator.Default"/>).
        /// </summary>
        /// <param name="text">Text, or null.</param>
        /// <returns>Estimated tokens.</returns>
        public static long EstimateTokens(string? text)
        {
            return CharacterRatioTokenEstimator.Default.CountTokens(text);
        }

        /// <summary>
        /// The budget for a reader's instructions file: its served context divided by <see cref="ContextDivisor"/>, less
        /// the tool schemas the launch sends, and at least <see cref="MinimumBudgetTokens"/>. Null when the context is
        /// unknown.
        /// </summary>
        /// <param name="reader">Reader, or null.</param>
        /// <param name="toolSchemaTokens">Estimated tokens of the tool schemas sent with every model turn.</param>
        /// <returns>Budget in tokens, or null.</returns>
        public static long? ResolveBudget(HandoffReader? reader, long toolSchemaTokens)
        {
            if (reader?.ContextTokens.HasValue != true || reader.ContextTokens!.Value <= 0) return null;
            long budget = reader.ContextTokens.Value / ContextDivisor - Math.Max(0, toolSchemaTokens);
            return Math.Max(MinimumBudgetTokens, budget);
        }

        /// <summary>
        /// Whether a template declares a slot, that is, contains <c>{name}</c>.
        /// </summary>
        /// <param name="templateContent">Template text, or null.</param>
        /// <param name="slot">Slot name without braces.</param>
        /// <returns>True when the template carries the slot.</returns>
        /// <exception cref="ArgumentNullException">Thrown when slot is null or empty.</exception>
        public static bool DeclaresSlot(string? templateContent, string slot)
        {
            if (String.IsNullOrEmpty(slot)) throw new ArgumentNullException(nameof(slot));
            return templateContent != null && templateContent.Contains("{" + slot + "}", StringComparison.Ordinal);
        }

        /// <summary>
        /// Join the sections in order and, when a budget is given, trim sections in rank order until the text plus the
        /// tool schemas fit. Rank-zero sections are never trimmed; if they alone exceed the budget the result is marked
        /// <see cref="InstructionsComposition.OverBudget"/>.
        /// </summary>
        /// <param name="sections">Sections in file order.</param>
        /// <param name="budgetTokens">Budget, or null for no limit.</param>
        /// <param name="toolSchemaTokens">Estimated tool schema tokens counted against the budget.</param>
        /// <returns>The composition.</returns>
        /// <exception cref="ArgumentNullException">Thrown when sections is null.</exception>
        public static InstructionsComposition Compose(IReadOnlyList<InstructionSection> sections, long? budgetTokens, long toolSchemaTokens = 0)
        {
            if (sections == null) throw new ArgumentNullException(nameof(sections));

            List<string> texts = sections.Select(s => s.Text ?? "").ToList();
            InstructionsComposition result = new InstructionsComposition { BudgetTokens = budgetTokens };
            long tools = Math.Max(0, toolSchemaTokens);
            long total = texts.Sum(t => EstimateTokens(t)) + tools;

            if (budgetTokens.HasValue)
            {
                // Ties keep file order: a later section of the same rank is trimmed after an earlier one.
                List<int> order = Enumerable.Range(0, sections.Count)
                    .Where(i => sections[i].TrimRank > 0 && !String.IsNullOrEmpty(sections[i].Text))
                    .OrderByDescending(i => sections[i].TrimRank)
                    .ThenBy(i => i)
                    .ToList();
                foreach (int i in order)
                {
                    if (total <= budgetTokens.Value) break;
                    string replacement = sections[i].TrimmedText ?? "";
                    total += EstimateTokens(replacement) - EstimateTokens(texts[i]);
                    texts[i] = replacement;
                    result.TrimmedSections.Add(sections[i].Name);
                }

                result.OverBudget = total > budgetTokens.Value;
            }

            StringBuilder text = new StringBuilder();
            foreach (string part in texts) text.Append(part);
            result.Text = text.ToString();
            result.EstimatedTokens = total;
            return result;
        }

        /// <summary>
        /// The note that replaces a trimmed section.
        /// </summary>
        /// <param name="what">What was left out, for example "The Skills section".</param>
        /// <param name="whereToFind">Where the captain can read it instead, or null.</param>
        /// <returns>The note, ending with a blank line.</returns>
        public static string TrimNote(string what, string? whereToFind = null)
        {
            string note = "_" + (what ?? "A section") + " was left out to fit this model's context budget."
                + (String.IsNullOrWhiteSpace(whereToFind) ? "" : " " + whereToFind!.Trim()) + "_\n\n";
            return note;
        }

        #endregion
    }
}
