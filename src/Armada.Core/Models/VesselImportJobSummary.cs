namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Summary stored as the ResultJson of a background vessel import job.
    /// </summary>
    public class VesselImportJobSummary
    {
        #region Public-Members

        /// <summary>
        /// Batch identifier (vib_ prefix). Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Vessels created. Minimum 0.
        /// </summary>
        public int CreatedCount
        {
            get => _CreatedCount;
            set => _CreatedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Candidates skipped. Minimum 0.
        /// </summary>
        public int SkippedCount
        {
            get => _SkippedCount;
            set => _SkippedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Candidates that failed. Minimum 0.
        /// </summary>
        public int FailedCount
        {
            get => _FailedCount;
            set => _FailedCount = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private int _CreatedCount = 0;
        private int _SkippedCount = 0;
        private int _FailedCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportJobSummary()
        {
        }

        #endregion
    }
}
