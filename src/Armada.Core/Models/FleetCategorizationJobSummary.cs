namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

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
        /// Validation warnings, for example vessel identifiers the captain invented. Never null.
        /// </summary>
        public List<string> Warnings
        {
            get => _Warnings;
            set => _Warnings = value ?? new List<string>();
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
