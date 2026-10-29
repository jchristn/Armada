namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Globalization;
    using Armada.Core.Models;

    /// <summary>
    /// Shared text for the black box screens: the canonical Ask Armada prompt, export file names, byte sizes, and the
    /// one-line "where" of an entry.
    /// </summary>
    public static class BlackBoxText
    {
        #region Public-Members

        /// <summary>
        /// The typed confirmation for a recovery path marked with a data-loss risk.
        /// </summary>
        public const string DataLossWord = "LOSE DATA";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The canonical Ask Armada prompt for a failure (plan section 10.3); a voyage id (vyg_) says "voyage".
        /// </summary>
        /// <param name="subjectId">Mission or voyage id (or the entry id when the entry has no subject).</param>
        /// <returns>Prompt.</returns>
        public static string AskPrompt(string subjectId)
        {
            string id = subjectId ?? "";
            string kind = id.StartsWith("vyg_", StringComparison.Ordinal) ? "voyage" : "mission";
            return "Show me the pertinent details from the black box for " + kind + " " + id + " and give me your opinion on why it failed and what could have been done differently";
        }

        /// <summary>
        /// The subject id an entry is about: its mission, else its voyage, else the entry itself.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>Id.</returns>
        public static string SubjectId(BlackBoxEntry entry)
        {
            if (entry == null) return "";
            if (!String.IsNullOrEmpty(entry.MissionId)) return entry.MissionId!;
            if (!String.IsNullOrEmpty(entry.VoyageId)) return entry.VoyageId!;
            return entry.Id;
        }

        /// <summary>
        /// Default export file name of a brief.
        /// </summary>
        /// <param name="entryId">Entry id.</param>
        /// <returns>File name.</returns>
        public static string BriefFileName(string entryId)
        {
            return "blackbox-" + (String.IsNullOrEmpty(entryId) ? "entry" : entryId) + ".md";
        }

        /// <summary>
        /// "Phase / Step" of an entry.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="step">Step.</param>
        /// <returns>Text.</returns>
        public static string Where(Armada.Core.Enums.BlackBoxPhaseEnum phase, Armada.Core.Enums.BlackBoxStepEnum step)
        {
            return phase + " / " + step;
        }

        /// <summary>
        /// A byte count in B, KB, or MB.
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        #endregion
    }
}
