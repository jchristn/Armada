namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Summary stored as the ResultJson of a background vessel discovery job.
    /// </summary>
    public class VesselDiscoveryJobSummary
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
        /// Candidates discovered. Minimum 0.
        /// </summary>
        public int CandidateCount
        {
            get => _CandidateCount;
            set => _CandidateCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// True when discovery stopped at the candidate cap.
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private int _CandidateCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselDiscoveryJobSummary()
        {
        }

        #endregion
    }
}
