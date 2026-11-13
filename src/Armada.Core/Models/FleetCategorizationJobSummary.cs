namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result payload stored on a FleetCategorization job when it succeeds.
    /// </summary>
    public class FleetCategorizationJobSummary
    {
        #region Public-Members

        /// <summary>
        /// Import batch identifier (vib_ prefix). Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Number of recommended fleets, including any Uncategorized bucket.
        /// </summary>
        public int FleetCount { get; set; } = 0;

        /// <summary>
        /// Number of vessels the captain analyzed.
        /// </summary>
        public int VesselCount { get; set; } = 0;

        /// <summary>
        /// Number of vessels the captain left unassigned (placed in the Uncategorized bucket).
        /// </summary>
        public int UncategorizedCount { get; set; } = 0;

        /// <summary>
        /// True when the recommendations were applied automatically.
        /// </summary>
        public bool Applied { get; set; } = false;

        /// <summary>
        /// Most entries in <see cref="Warnings"/>, including a final "N more" summary line.
        /// </summary>
        public const int MaxWarnings = 50;

        /// <summary>
        /// Validation warnings, for example vessel identifiers the captain invented: the first ones, then one line
        /// saying how many more were not listed (see <see cref="BoundWarnings"/>). Never null.
        /// </summary>
        [CollectionBound(MaxWarnings, Reason = "First warnings plus one summary line for the rest")]
        public List<string> Warnings
        {
            get => _Warnings;
            set => _Warnings = value ?? new List<string>();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Keep the first warnings and replace the rest with one line that says how many more there were, so the
        /// list holds at most <see cref="MaxWarnings"/> entries and nothing is hidden.
        /// </summary>
        /// <param name="warnings">Every warning, or null.</param>
        /// <returns>The bounded list. Never null.</returns>
        public static List<string> BoundWarnings(List<string>? warnings)
        {
            if (warnings == null) return new List<string>();
            if (warnings.Count <= MaxWarnings) return new List<string>(warnings);
            List<string> bounded = warnings.GetRange(0, MaxWarnings - 1);
            bounded.Add((warnings.Count - (MaxWarnings - 1)) + " more warnings were not listed.");
            return bounded;
        }

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private List<string> _Warnings = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetCategorizationJobSummary()
        {
        }

        #endregion
    }
}
