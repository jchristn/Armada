namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Pure helper for mission-mode semantics: whether a mode is read-only, and the report-shaped brief
    /// contract injected for read-only modes. Kept side-effect free so it unit-tests without a dock or DB.
    /// </summary>
    public static class MissionModeContract
    {
        #region Public-Methods

        /// <summary>
        /// Whether the mode is read-only. Read-only modes (Audit, Research) are expected to produce a written
        /// report rather than a commit; the landing gate treats an empty diff as success for them.
        /// </summary>
        /// <param name="mode">Mission mode.</param>
        /// <returns>True when the mode does not expect a committed change.</returns>
        public static bool IsReadOnly(MissionModeEnum mode)
        {
            return mode == MissionModeEnum.Audit || mode == MissionModeEnum.Research;
        }

        /// <summary>
        /// Build the read-only brief section for a mission mode, or an empty string for write modes. The
        /// section instructs the captain not to modify the repository and to deliver a written report,
        /// ending with a standalone completion marker so no commit is expected.
        /// </summary>
        /// <param name="mode">Mission mode.</param>
        /// <returns>Markdown section text, or an empty string when the mode is a write mode.</returns>
        public static string BuildBriefSection(MissionModeEnum mode)
        {
            if (!IsReadOnly(mode)) return String.Empty;

            string label = mode == MissionModeEnum.Audit ? "Audit" : "Research";
            string focus = mode == MissionModeEnum.Audit
                ? "Inspect the code, tests, and docs in scope and report what you find -- defects, risks, gaps, and stale content."
                : "Investigate the question in the mission description and report your conclusions, with the evidence (files, symbols, commits) that supports them.";

            return
                "## Mission Mode: " + label + " (READ-ONLY)\n\n" +
                "This is a READ-ONLY " + label.ToLowerInvariant() + " mission. Do NOT modify, create, delete, or rename any files, " +
                "and do NOT run commands that change the working tree or push commits. " + focus + "\n\n" +
                "Deliver your findings as a written report in your final response using clear Markdown headings. " +
                "No commit is expected -- an empty diff is a successful outcome for this mission. " +
                "End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.\n";
        }

        #endregion
    }
}
